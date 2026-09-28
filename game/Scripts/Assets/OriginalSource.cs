using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenGG.Core.Original;

namespace OpenGG.Assets;

/// <summary>
/// Models, pictures and decals read at runtime from the player's own copy of the original game.
/// Ids: "orig:&lt;car&gt;/&lt;mesh&gt;", "orig:&lt;car&gt;/@static/&lt;region&gt;", "orig-portrait:…", "orig-decal:…".
/// </summary>
public sealed class OriginalSource(OriginalGame game) : IAssetSource
{
    const int KeepCars = 6;
    readonly Dictionary<string, OrigCarData> cars = [];
    readonly LinkedList<string> recent = new();
    readonly Dictionary<string, Dictionary<int, Material>> materials = [];
    readonly Dictionary<string, Dictionary<int, Texture2D?>> textures = [];

    OrigCarData? Car(string carId)
    {
        if (cars.TryGetValue(carId, out var data))
        {
            recent.Remove(carId);
            recent.AddFirst(carId);
            return data;
        }
        if (!game.Cars.TryGetValue(carId, out var info)) return null;
        data = CarFile.ReadFull(info.Path);
        cars[carId] = data;
        recent.AddFirst(carId);
        while (recent.Count > KeepCars)
        {
            var old = recent.Last!.Value;
            recent.RemoveLast();
            cars.Remove(old);
        }
        return data;
    }

    public ModelData? Model(string id)
    {
        if (!id.StartsWith("orig:", StringComparison.Ordinal)) return null;
        int slash = id.IndexOf('/');
        if (slash < 0) return null;
        string carId = id[5..slash], mesh = id[(slash + 1)..];
        if (Car(carId) is not { } data) return null;
        var model = new ModelData();
        if (mesh.StartsWith("@static/", StringComparison.Ordinal))
        {
            char prefix = mesh[8..] switch { "engine" => '#', "running_gear" => '^', _ => '$' };
            var used = data.Info.Parts.Select(p => p.Mesh).ToHashSet();
            foreach (var m in data.Meshes.Values)
                if (m.Name.Length > 1 && m.Name[0] == prefix && !used.Contains(m.Name)) AddMesh(model, carId, data, m);
        }
        else if (data.Meshes.TryGetValue(mesh, out var m)) AddMesh(model, carId, data, m);
        return model.Pieces.Count > 0 ? model : null;
    }

    void AddMesh(ModelData model, string carId, OrigCarData data, OrigMesh m)
    {
        var byMaterial = new Dictionary<int, List<int>>();
        for (int t = 0; t < m.Materials.Length; t++)
        {
            if (!byMaterial.TryGetValue(m.Materials[t], out var list)) byMaterial[m.Materials[t]] = list = [];
            list.Add(t);
        }
        var mesh = new ArrayMesh();
        foreach (var (matId, tris) in byMaterial.OrderBy(k => k.Key))
        {
            if (matId != OrigMesh.Paint && matId < data.Materials.Count && data.Materials[matId].Blend == 2) continue; // additive overlays
            bool flat = matId != OrigMesh.Paint && matId < data.Materials.Count && data.Materials[matId].Flat;
            var verts = new Vector3[tris.Count * 3];
            var norms = new Vector3[tris.Count * 3];
            var faceNorms = new Color[tris.Count * 3];
            var uvs = new Vector2[tris.Count * 3];
            int k = 0;
            foreach (int t in tris)
            {
                int ia = m.Triangles[t * 3], ib = m.Triangles[t * 3 + 1], ic = m.Triangles[t * 3 + 2];
                var a = P(m, ia);
                var b = P(m, ib);
                var c = P(m, ic);
                // The original draws every face from both sides (OrigLook), lit by its normals: a face seen from
                // its back (a floor pan from below, a window whose faces point into the cab, the 0.4 % of faces
                // whose normals are against their corners' order) is lit from behind and comes out dark.
                // Mirrored into our space the order is already Godot's front.
                int[] order = [0, 1, 2];
                int[] idx = [ia, ib, ic];
                // Measured: a face of a flat-shaded material (the boxy engine parts, a pickup's bed) is lit by one
                // normal of its own, taken from its corners the other way round from the normals stored with them (so
                // a bed's floor and inner walls come out dark in the WorkShop, as the original draws them), not by
                // its corners' stored normals, which are averaged over the corners of the box. The flat materials'
                // shader reads it from the vertex colour; drawn as bare metal the part is smooth, by the stored ones.
                var own = (c - a).Cross(b - a);
                var faceNormal = flat && own.LengthSquared() > 1e-12f ? own.Normalized() : (Vector3?)null;
                foreach (int o in order)
                {
                    verts[k] = P(m, idx[o]);
                    norms[k] = N(m, idx[o]);
                    var fn = faceNormal ?? norms[k];
                    faceNorms[k] = new Color(fn.X * 0.5f + 0.5f, fn.Y * 0.5f + 0.5f, fn.Z * 0.5f + 0.5f);
                    float u = m.Uvs[t * 6 + o * 2], v = m.Uvs[t * 6 + o * 2 + 1];
                    uvs[k] = float.IsNaN(u) || float.IsNaN(v) ? Vector2.Zero : new Vector2(u, v);
                    k++;
                }
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = norms;
            arrays[(int)Mesh.ArrayType.Color] = faceNorms;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Material(carId, data, matId, shiny: m.Name.StartsWith('$')));
        }
        if (mesh.GetSurfaceCount() == 0) return;
        var r = m.Rows;
        var basis = new Basis(
            new Vector3((float)r[0].X, (float)r[0].Y, (float)r[0].Z),
            new Vector3((float)r[1].X, (float)r[1].Y, (float)r[1].Z),
            new Vector3((float)r[2].X, (float)r[2].Y, (float)r[2].Z));
        model.Add(mesh, new Transform3D(basis, new Vector3((float)m.Pivot.X, (float)m.Pivot.Y, (float)m.Pivot.Z)));
    }

    static Vector3 P(OrigMesh m, int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
    static Vector3 N(OrigMesh m, int i) => new(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);

    /// <summary>A material of the car, lit as the original lights it (<see cref="View3D.OrigLook"/>); the body
    /// meshes' materials get the highlight.</summary>
    Material Material(string carId, OrigCarData data, int id, bool shiny)
    {
        if (!materials.TryGetValue(carId, out var map)) materials[carId] = map = [];
        int key = shiny ? ~id : id;
        if (map.TryGetValue(key, out var cached)) return cached;
        Material mat;
        if (id == OrigMesh.Paint || id >= data.Materials.Count)
        {
            // The car's paint. VehicleView swaps in the paint colour by this name.
            mat = View3D.OrigLook.Material("paint", new Color(0.5f, 0.5f, 0.5f), null, View3D.OrigLook.Blend.Opaque, flat: false, shiny);
        }
        else
        {
            var o = data.Materials[id];
            var blend = o.Blend == 2 ? View3D.OrigLook.Blend.Add : o.Opacity < 0.999f || o.Blend == 1 ? View3D.OrigLook.Blend.Alpha : View3D.OrigLook.Blend.Opaque;
            var tex = o.Texture >= 0 ? Texture(carId, data, o.Texture) : null;
            // A textured material's own colour (always one half in the .car files) is not used: its texture is lit.
            var color = tex is null ? new Color(o.R, o.G, o.B, Mathf.Clamp(o.Opacity, 0, 1)) : new Color(1, 1, 1, Mathf.Clamp(o.Opacity, 0, 1));
            mat = View3D.OrigLook.Material(o.Name, color, tex, blend, o.Flat, shiny);
        }
        ((ShaderMaterial)mat).SetShaderParameter("mat_id", id == OrigMesh.Paint || id >= data.Materials.Count ? 666 : id);
        map[key] = mat;
        return mat;
    }

    Texture2D? Texture(string carId, OrigCarData data, int index)
    {
        if (!textures.TryGetValue(carId, out var map)) textures[carId] = map = [];
        if (map.TryGetValue(index, out var t)) return t;
        t = index < data.Textures.Count && data.Textures[index] is { } img ? ToTexture(img, mipmaps: true) : null;
        map[index] = t;
        return t;
    }

    static ImageTexture ToTexture(OrigImage img, bool mipmaps)
    {
        var image = Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba);
        if (mipmaps) image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    public Texture2D? Texture(string id) => Picture(id) is { } image ? ToTexture(image, mipmaps: true) : null;

    public Texture2D? Icon(string id, int size)
    {
        if (Picture(id) is not { } image) return null;
        int w = image.GetWidth(), h = image.GetHeight();
        float scale = size / (float)Math.Max(w, h);
        if (Math.Abs(scale - 1) > 0.01f) image.Resize(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)), Image.Interpolation.Bilinear);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A picture of the original: a portrait or decal read with its car or job ("orig-portrait:", "orig-decal:"),
    /// or a face of the random jobs from the game's archive ("orig-face:random/&lt;file&gt;").</summary>
    Image? Picture(string id)
    {
        if (id.StartsWith("orig-face:", StringComparison.Ordinal))
        {
            var path = id[10..];
            if (game.Archives?.Read(path) is not { } bytes) return null;
            var image = new Image();
            var err = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(bytes),
                ".tga" => image.LoadTgaFromBuffer(bytes),
                ".bmp" => image.LoadBmpFromBuffer(bytes),
                ".png" => image.LoadPngFromBuffer(bytes),
                _ => Error.FileUnrecognized,
            };
            if (err != Error.Ok) return null;
            image.Convert(Image.Format.Rgba8);
            return image;
        }
        if (!id.StartsWith("orig-", StringComparison.Ordinal) || !game.Pictures.TryGetValue(id, out var img)) return null;
        return Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba);
    }

    static ImageTexture ToTexture(Image image, bool mipmaps)
    {
        if (mipmaps) image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A part's own sound: "orig-sound:&lt;car&gt;/&lt;part number&gt;".</summary>
    public AudioStream? Sound(string id)
    {
        if (!id.StartsWith("orig-sound:", StringComparison.Ordinal)) return null;
        int slash = id.LastIndexOf('/');
        if (slash < 0 || !int.TryParse(id[(slash + 1)..], out int number)) return null;
        var carId = id[11..slash];
        return Car(carId) is { } data && data.Sounds.TryGetValue(number, out var wav) ? Wav.Load(wav) : null;
    }
}
