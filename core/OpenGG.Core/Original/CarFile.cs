// The original's .car files: a car's parts, meshes, materials and textures.
// Layout: docs/formats/tagged-files.md (".car").

using OpenGG.Core.Content;

namespace OpenGG.Core.Original;

public sealed class OrigPart
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public bool Custom { get; init; }
    /// <summary>1 engine, 2 body, 3 running gear.</summary>
    public int Region { get; init; }
    public string Mesh { get; init; } = "";
    public float CostMin { get; init; }
    public float CostMax { get; init; }
    /// <summary>Parts this one is mounted on (AttachDep).</summary>
    public int[] AttachDep { get; init; } = [];
    /// <summary>Parts that must come off first (RemoveDep).</summary>
    public int[] RemoveDep { get; init; } = [];
    public int MutualExc { get; init; }
    public bool Amea { get; init; }
    /// <summary>0 none, 1 block, 2 starter, 3 spinner, 4 accessory.</summary>
    public int Special { get; init; }
    /// <summary>Bolt positions in car space (OpenGG meters).</summary>
    public List<Vec3> Bolts { get; init; } = [];
}

public class OrigMeshInfo
{
    public string Name { get; init; } = "";
    public int Index { get; init; }
    /// <summary>Pivot in car space (OpenGG meters).</summary>
    public Vec3 Pivot { get; init; }
    /// <summary>Local X, Y, Z axes in car space (OpenGG orientation).</summary>
    public Vec3[] Rows { get; init; } = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
}

public sealed class OrigMesh : OrigMeshInfo
{
    /// <summary>x, y, z per vertex, local, OpenGG meters.</summary>
    public float[] Positions { get; init; } = [];
    public float[] Normals { get; init; } = [];
    /// <summary>Three vertex indices per triangle, in the file's order (see <see cref="OrigSpace"/> for winding).</summary>
    public int[] Triangles { get; init; } = [];
    /// <summary>u, v for each corner (6 per triangle); NaN where the file has no value.</summary>
    public float[] Uvs { get; init; } = [];
    /// <summary>Material id per triangle; 666 is the car's paint.</summary>
    public int[] Materials { get; init; } = [];

    public const int Paint = 666;
}

public sealed class OrigMaterial
{
    public string Name { get; init; } = "";
    public float R { get; init; } = 1;
    public float G { get; init; } = 1;
    public float B { get; init; } = 1;
    public float Opacity { get; init; } = 1;
    /// <summary>0 opaque, 1 alpha-blended, 2 additive.</summary>
    public int Blend { get; init; }
    /// <summary>Index into the car's textures, or -1.</summary>
    public int Texture { get; init; } = -1;
    /// <summary>605: true for flat shading (1), false for smooth (2).</summary>
    public bool Flat { get; init; }
    /// <summary>607: 0 matte, 1 overlay, 2 body and paint (the default), 3 glass.</summary>
    public int Class { get; init; } = 2;
}

public sealed class OrigCarInfo
{
    public string Path { get; init; } = "";
    public int CarId { get; init; }
    public string Name { get; init; } = "";
    public int MinSkill { get; init; }
    public List<OrigPart> Parts { get; } = [];
    public List<OrigMeshInfo> Meshes { get; } = [];
    /// <summary>Parts that come with their own sound (a WAV in the file).</summary>
    public HashSet<int> SoundParts { get; } = [];
    /// <summary>The car's own paint: the colour of its paint materials (<c>MultiMatPaint</c>), as "#rrggbb".</summary>
    public string? OwnPaint { get; set; }
}

public sealed class OrigCarData
{
    public required OrigCarInfo Info { get; init; }
    public List<OrigImage?> Textures { get; } = [];
    public List<OrigMaterial> Materials { get; } = [];
    /// <summary>Meshes by name (first of each name; overlay meshes are left out).</summary>
    public Dictionary<string, OrigMesh> Meshes { get; } = [];
    /// <summary>Per-part sounds: part number → WAV file bytes.</summary>
    public Dictionary<int, byte[]> Sounds { get; } = [];
}

public static class CarFile
{
    /// <summary>Car name, id, parts and mesh placement, skipping textures and geometry. Fast.</summary>
    public static OrigCarInfo ReadInfo(string path)
    {
        using var r = TagReader.Open(path);
        return Read(r, path, full: null);
    }

    /// <summary>Everything, for drawing the car.</summary>
    public static OrigCarData ReadFull(string path)
    {
        using var r = TagReader.Open(path, inMemory: true);
        OrigCarData? data = null;
        Read(r, path, full: info => data = new OrigCarData { Info = info });
        return data!;
    }

    static OrigCarInfo Read(TagReader r, string path, Func<OrigCarInfo, OrigCarData>? full)
    {
        var root = r.Root();
        if (root.Id != 1 || root.Type != TagType.Node) throw new InvalidDataException($"{path} is not a car file.");
        var carNode = r.Child(root, 100) ?? throw new InvalidDataException($"{path} has no car.");
        int carId = 0, minSkill = 0;
        string name = "";
        foreach (var c in r.Children(carNode))
        {
            if (c.Id == 200) carId = r.Int(c);
            else if (c.Id == 201) name = r.String(c).Trim();
            else if (c.Id == 210) minSkill = r.Int(c);
        }
        var info = new OrigCarInfo { Path = path, CarId = carId, Name = name, MinSkill = minSkill };
        var data = full?.Invoke(info);
        foreach (var c in r.Children(carNode))
            switch (c.Id)
            {
                case 202 when data is not null:
                    data.Textures.Add(r.Child(c, 402) is { } img ? OrigImage.Read(r, img) : null);
                    break;
                case 203:
                    var mat = ReadMaterial(r, c);
                    data?.Materials.Add(mat);
                    if (info.OwnPaint is null && mat.Name == "MultiMatPaint")
                        info.OwnPaint = $"#{Channel(mat.R):x2}{Channel(mat.G):x2}{Channel(mat.B):x2}";
                    break;
                case 204:
                    var mesh = ReadMesh(r, c, geometry: data is not null);
                    info.Meshes.Add(mesh);
                    if (data is not null && mesh is OrigMesh m) data.Meshes.TryAdd(m.Name, m);
                    break;
                case 206:
                    info.Parts.Add(ReadPart(r, c));
                    break;
                case 209:
                    // Some fan cars list a part here without the WAV; those have no sound of their own.
                    int number = -1;
                    bool hasWav = false;
                    byte[]? wav = null;
                    foreach (var s in r.Children(c))
                        if (s.Id == 2100) number = r.Int(s);
                        else if (s.Id == 2101)
                        {
                            hasWav = s.Length > 44;
                            if (data is not null) wav = r.Bytes(s);
                        }
                    if (number >= 0 && hasWav) info.SoundParts.Add(number);
                    if (number >= 0 && wav is not null) data!.Sounds[number] = wav;
                    break;
            }
        return info;
    }

    static int Channel(float v) => (int)Math.Round(Math.Clamp(v, 0f, 1f) * 255);

    static OrigMaterial ReadMaterial(TagReader r, TagHeader node)
    {
        string name = "";
        float cr = 1, cg = 1, cb = 1, opacity = 1;
        int blend = 0, texture = -1, shade = 2, kind = 2;
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 600: name = r.String(c); break;
                case 605: shade = r.Int(c); break;
                case 607: kind = r.Int(c); break;
                case 601:
                    var (x, y, z) = r.Vec(c);
                    if (!float.IsNaN(x)) (cr, cg, cb) = (x, y, z);
                    break;
                case 604: blend = r.Int(c); break;
                case 606:
                    float o = r.Float(c);
                    if (!float.IsNaN(o)) opacity = o;
                    break;
                case 608: texture = r.Int(c); break;
            }
        return new OrigMaterial { Name = name, R = cr, G = cg, B = cb, Opacity = opacity, Blend = blend, Texture = texture, Flat = shade == 1, Class = kind };
    }

    static OrigMeshInfo ReadMesh(TagReader r, TagHeader node, bool geometry)
    {
        string name = "";
        int index = 0;
        Vec3 pivot = Vec3.Zero;
        Vec3 r0 = new(1, 0, 0), r1 = new(0, 1, 0), r2 = new(0, 0, 1);
        var pos = new List<float>();
        var nrm = new List<float>();
        var tris = new List<int>();
        var uvs = new List<float>();
        var mats = new List<int>();
        foreach (var c in r.Children(node))
        {
            switch (c.Id)
            {
                case 700: name = r.String(c); continue;
                case 701: index = r.Int(c); continue;
                case 702: pivot = OrigSpace.Point(r.Vec(c)); continue;
                case 703: r0 = V(r.Vec(c)); continue;
                case 704: r1 = V(r.Vec(c)); continue;
                case 705: r2 = V(r.Vec(c)); continue;
            }
            if (!geometry && c.Id is 708 or 709) break; // placement is all we need
            if (c.Id == 708)
            {
                var (px, py, pz, nx, ny, nz) = (0f, 0f, 0f, 0f, 1f, 0f);
                foreach (var v in r.Children(c))
                    if (v.Id == 900) (px, py, pz) = r.Vec(v);
                    else if (v.Id == 901) (nx, ny, nz) = r.Vec(v);
                var p = OrigSpace.Point(px, py, pz);
                pos.Add((float)p.X);
                pos.Add((float)p.Y);
                pos.Add((float)p.Z);
                var n = OrigSpace.Direction(float.IsNaN(nx) ? 0 : nx, float.IsNaN(ny) ? 1 : ny, float.IsNaN(nz) ? 0 : nz);
                nrm.Add((float)n.X);
                nrm.Add((float)n.Y);
                nrm.Add((float)n.Z);
            }
            else if (c.Id == 709)
            {
                int a = 0, b = 0, d = 0, mat = 0;
                float[] uv = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];
                foreach (var f in r.Children(c))
                    switch (f.Id)
                    {
                        case 800: a = r.Int(f); break;
                        case 801: b = r.Int(f); break;
                        case 802: d = r.Int(f); break;
                        case 803 or 804 or 805:
                            var t = r.Floats(f);
                            if (t.Length >= 2)
                            {
                                uv[(f.Id - 803) * 2] = t[0];
                                uv[(f.Id - 803) * 2 + 1] = t[1];
                            }
                            break;
                        case 806: mat = r.Int(f); break;
                    }
                tris.Add(a);
                tris.Add(b);
                tris.Add(d);
                uvs.AddRange(uv);
                mats.Add(mat);
            }
        }
        var rows = OrigSpace.Rows(r0, r1, r2);
        if (!geometry) return new OrigMeshInfo { Name = name, Index = index, Pivot = pivot, Rows = rows };
        int vc = pos.Count / 3;
        // Drop triangles that point past the vertex list.
        var goodTris = new List<int>();
        var goodUvs = new List<float>();
        var goodMats = new List<int>();
        for (int i = 0; i < mats.Count; i++)
        {
            int a = tris[i * 3], b = tris[i * 3 + 1], d = tris[i * 3 + 2];
            if (a < 0 || b < 0 || d < 0 || a >= vc || b >= vc || d >= vc) continue;
            goodTris.AddRange([a, b, d]);
            for (int k = 0; k < 6; k++) goodUvs.Add(uvs[i * 6 + k]);
            goodMats.Add(mats[i]);
        }
        return new OrigMesh
        {
            Name = name,
            Index = index,
            Pivot = pivot,
            Rows = rows,
            Positions = [.. pos],
            Normals = [.. nrm],
            Triangles = [.. goodTris],
            Uvs = [.. goodUvs],
            Materials = [.. goodMats],
        };

        static Vec3 V((float X, float Y, float Z) v) => float.IsNaN(v.X) ? Vec3.Zero : new Vec3(v.X, v.Y, v.Z);
    }

    static OrigPart ReadPart(TagReader r, TagHeader node)
    {
        int number = 0, region = 0, mutual = 0, special = 0;
        bool custom = false, amea = false;
        string name = "", mesh = "";
        float min = 0, max = 0;
        int[] attach = [], remove = [];
        var bolts = new List<Vec3>();
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 300: number = r.Int(c); break;
                case 301: name = r.String(c).Trim(); break;
                case 302: custom = r.Int(c) != 0; break;
                case 303: region = r.Int(c); break;
                case 304: mesh = r.String(c); break;
                case 305: min = r.Float(c); break;
                case 306: max = r.Float(c); break;
                case 307: attach = r.Ints(c); break;
                case 308: remove = r.Ints(c); break;
                case 309: mutual = r.Int(c); break;
                case 310: amea = r.Int(c) != 0; break;
                case 312: special = r.Int(c); break;
                case 313:
                    var v = r.Vec(c);
                    if (!float.IsNaN(v.X)) bolts.Add(OrigSpace.Point(v));
                    break;
            }
        if (float.IsNaN(min)) min = 0;
        if (float.IsNaN(max) || max < min) max = Math.Max(min, 0);
        return new OrigPart
        {
            Number = number, Name = name, Custom = custom, Region = region, Mesh = mesh,
            CostMin = min, CostMax = max, AttachDep = attach, RemoveDep = remove,
            MutualExc = mutual, Amea = amea, Special = special, Bolts = bolts,
        };
    }
}
