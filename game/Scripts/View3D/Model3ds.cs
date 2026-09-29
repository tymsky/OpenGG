using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace OpenGG.View3D;

/// <summary>
/// A 3D Studio (.3ds) file: meshes with their materials, cameras and lights, in the file's own units
/// with Z up. <see cref="Build"/> turns it into Godot meshes.
/// </summary>
public sealed class Model3ds
{
    public sealed class Material
    {
        public string Name = "";
        public Color Ambient = Colors.Black;
        public Color Diffuse = Colors.White;
        public string? Texture;
        public float UScale = 1, VScale = 1, UOffset, VOffset;
        public bool TwoSided;
        /// <summary>0 opaque .. 1 invisible.</summary>
        public float Transparency;
    }

    public sealed class Mesh
    {
        public string Name = "";
        public Vector3[] Verts = [];
        public Vector2[]? Uvs;
        /// <summary>Three vertex indices per face.</summary>
        public int[] Faces = [];
        public string?[] FaceMaterial = [];
        public uint[]? Smoothing;
        /// <summary>The object's own axes and origin when it was saved (the vertices are in the file's space).</summary>
        public Transform3D? Matrix;

        public Aabb Bounds()
        {
            if (Verts.Length == 0) return new Aabb();
            var box = new Aabb(Verts[0], Vector3.Zero);
            foreach (var v in Verts) box = box.Expand(v);
            return box;
        }
    }

    public sealed class Camera
    {
        public string Name = "";
        public Vector3 Position, Target;
        public float Bank;
        /// <summary>Lens in millimetres (35 mm film).</summary>
        public float Lens = 50;

        /// <summary>Horizontal field of view in degrees.</summary>
        public float HorizontalFov => 2 * Mathf.RadToDeg(Mathf.Atan(18f / Lens));
    }

    public sealed class Light
    {
        public string Name = "";
        public Vector3 Position;
        public Color Color = Colors.White;
    }

    /// <summary>An object's place in the keyframer (its first keys): where it really is.</summary>
    sealed class Node
    {
        public string Name = "";
        public int Id = -1, Parent = -1;
        public Vector3 Pivot, Position, Scale = Vector3.One, Axis = Vector3.Up;
        public float Angle;
    }

    readonly List<Node> nodes = [];

    public List<Material> Materials { get; } = [];
    public List<Mesh> Meshes { get; } = [];
    public List<Camera> Cameras { get; } = [];
    public List<Light> Lights { get; } = [];

    public Mesh? Find(string name) => Meshes.Find(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lab: the placed meshes (the file's space), their faces, texture coordinates and materials, and the
    /// materials and lights, as JSON (for fitting the original's look outside Godot).</summary>
    public string ToJson()
    {
        static float[] V(Vector3 v) => [v.X, v.Y, v.Z];
        static float[] C(Color c) => [c.R, c.G, c.B];
        var doc = new
        {
            meshes = Meshes.Select(m => new
            {
                name = m.Name,
                verts = m.Verts.Select(V).ToArray(),
                faces = m.Faces,
                uvs = m.Uvs?.Select(u => new[] { u.X, u.Y }).ToArray(),
                faceMaterial = m.FaceMaterial,
            }),
            materials = Materials.Select(x => new { name = x.Name, ambient = C(x.Ambient), diffuse = C(x.Diffuse), texture = x.Texture, uScale = x.UScale, vScale = x.VScale, uOffset = x.UOffset, vOffset = x.VOffset, twoSided = x.TwoSided, transparency = x.Transparency }),
            lights = Lights.Select(l => new { name = l.Name, position = V(l.Position), color = C(l.Color) }),
            cameras = Cameras.Select(c => new { name = c.Name, position = V(c.Position), target = V(c.Target), lens = c.Lens }),
        };
        return System.Text.Json.JsonSerializer.Serialize(doc);
    }

    // ---- reading ---------------------------------------------------------------------------------------

    public static Model3ds Read(byte[] data)
    {
        var m = new Model3ds();
        m.Chunks(data, 0, data.Length, null);
        m.Place();
        return m;
    }

    /// <summary>
    /// Moves every mesh to where the keyframer puts it: the vertices go back into the object's own space
    /// (by the matrix saved with the mesh), then through pivot, scale, rotation and position. (3ds keeps
    /// the angle the other way round.)
    /// </summary>
    void Place()
    {
        Transform3D NodeMatrix(Node n, int depth = 0)
        {
            var t = new Transform3D(new Basis(n.Axis.LengthSquared() > 1e-8f ? n.Axis.Normalized() : Vector3.Up, -n.Angle).Scaled(n.Scale), n.Position);
            if (n.Parent >= 0 && depth < 16 && nodes.Find(p => p.Id == n.Parent) is { } parent) t = NodeMatrix(parent, depth + 1) * t;
            return t;
        }
        foreach (var mesh in Meshes)
        {
            if (mesh.Matrix is not { } saved || nodes.Find(n => n.Name == mesh.Name) is not { } node) continue;
            var toWorld = NodeMatrix(node) * new Transform3D(Basis.Identity, -node.Pivot) * saved.AffineInverse();
            for (int i = 0; i < mesh.Verts.Length; i++) mesh.Verts[i] = toWorld * mesh.Verts[i];
        }
    }

    void Chunks(byte[] b, int start, int end, object? owner)
    {
        int p = start;
        while (p + 6 <= end)
        {
            int id = BitConverter.ToUInt16(b, p);
            int len = BitConverter.ToInt32(b, p + 2);
            if (len < 6 || p + len > end) break;
            int body = p + 6, stop = p + len;
            switch (id)
            {
                case 0x4D4D: // main
                case 0x3D3D: // editor
                case 0xB000: // keyframer
                    Chunks(b, body, stop, null);
                    break;
                case 0xB002: // object node
                {
                    var node = new Node();
                    Chunks(b, body, stop, node);
                    nodes.Add(node);
                    break;
                }
                case 0xB030 when owner is Node node:
                    node.Id = BitConverter.ToInt16(b, body);
                    break;
                case 0xB010 when owner is Node node:
                {
                    int z = Array.IndexOf(b, (byte)0, body, stop - body);
                    node.Name = Encoding.Latin1.GetString(b, body, z - body);
                    node.Parent = BitConverter.ToInt16(b, z + 5);
                    break;
                }
                case 0xB013 when owner is Node node:
                    node.Pivot = new Vector3(F(b, body), F(b, body + 4), F(b, body + 8));
                    break;
                case 0xB020 or 0xB021 or 0xB022 when owner is Node node:
                {
                    // Track: flags, two unknown words, key count; first key: frame, flags, spline values, value.
                    if (BitConverter.ToInt32(b, body + 10) < 1) break;
                    int q = body + 14 + 4;
                    int kflags = BitConverter.ToUInt16(b, q);
                    q += 2;
                    for (int bit = 0; bit < 5; bit++) if ((kflags & (1 << bit)) != 0) q += 4;
                    if (id == 0xB020) node.Position = new Vector3(F(b, q), F(b, q + 4), F(b, q + 8));
                    else if (id == 0xB022) node.Scale = new Vector3(F(b, q), F(b, q + 4), F(b, q + 8));
                    else
                    {
                        node.Angle = F(b, q);
                        node.Axis = new Vector3(F(b, q + 4), F(b, q + 8), F(b, q + 12));
                    }
                    break;
                }
                case 0x4000: // named object
                {
                    int z = Array.IndexOf(b, (byte)0, body, stop - body);
                    string name = Encoding.Latin1.GetString(b, body, Math.Max(0, z - body));
                    Chunks(b, z + 1, stop, name);
                    break;
                }
                case 0x4100 when owner is string name: // triangle mesh
                {
                    var mesh = new Mesh { Name = name };
                    Meshes.Add(mesh);
                    Chunks(b, body, stop, mesh);
                    break;
                }
                case 0x4110 when owner is Mesh mesh: // vertices
                {
                    int n = BitConverter.ToUInt16(b, body);
                    mesh.Verts = new Vector3[n];
                    for (int i = 0; i < n; i++)
                        mesh.Verts[i] = new Vector3(F(b, body + 2 + i * 12), F(b, body + 6 + i * 12), F(b, body + 10 + i * 12));
                    break;
                }
                case 0x4140 when owner is Mesh mesh: // texture coordinates
                {
                    int n = BitConverter.ToUInt16(b, body);
                    mesh.Uvs = new Vector2[n];
                    for (int i = 0; i < n; i++) mesh.Uvs[i] = new Vector2(F(b, body + 2 + i * 8), F(b, body + 6 + i * 8));
                    break;
                }
                case 0x4120 when owner is Mesh mesh: // faces, then their materials and smoothing groups
                {
                    int n = BitConverter.ToUInt16(b, body);
                    mesh.Faces = new int[n * 3];
                    mesh.FaceMaterial = new string?[n];
                    for (int i = 0; i < n; i++)
                        for (int k = 0; k < 3; k++)
                            mesh.Faces[i * 3 + k] = BitConverter.ToUInt16(b, body + 2 + i * 8 + k * 2);
                    Chunks(b, body + 2 + n * 8, stop, mesh);
                    break;
                }
                case 0x4130 when owner is Mesh mesh: // faces of a material
                {
                    int z = Array.IndexOf(b, (byte)0, body, stop - body);
                    string mat = Encoding.Latin1.GetString(b, body, z - body);
                    int n = BitConverter.ToUInt16(b, z + 1);
                    for (int i = 0; i < n; i++)
                    {
                        int f = BitConverter.ToUInt16(b, z + 3 + i * 2);
                        if (f < mesh.FaceMaterial.Length) mesh.FaceMaterial[f] = mat;
                    }
                    break;
                }
                case 0x4160 when owner is Mesh mesh: // the object's matrix: X, Y, Z axes and origin
                    mesh.Matrix = new Transform3D(
                        new Basis(new Vector3(F(b, body), F(b, body + 4), F(b, body + 8)), new Vector3(F(b, body + 12), F(b, body + 16), F(b, body + 20)), new Vector3(F(b, body + 24), F(b, body + 28), F(b, body + 32))),
                        new Vector3(F(b, body + 36), F(b, body + 40), F(b, body + 44)));
                    break;
                case 0x4150 when owner is Mesh mesh: // smoothing groups
                {
                    int n = mesh.FaceMaterial.Length;
                    mesh.Smoothing = new uint[n];
                    for (int i = 0; i < n && body + i * 4 + 4 <= stop; i++) mesh.Smoothing[i] = BitConverter.ToUInt32(b, body + i * 4);
                    break;
                }
                case 0x4700 when owner is string name: // camera
                    Cameras.Add(new Camera
                    {
                        Name = name,
                        Position = new Vector3(F(b, body), F(b, body + 4), F(b, body + 8)),
                        Target = new Vector3(F(b, body + 12), F(b, body + 16), F(b, body + 20)),
                        Bank = F(b, body + 24),
                        Lens = F(b, body + 28),
                    });
                    break;
                case 0x4600 when owner is string name: // light
                {
                    var light = new Light { Name = name, Position = new Vector3(F(b, body), F(b, body + 4), F(b, body + 8)) };
                    Lights.Add(light);
                    ReadColor(b, body + 12, stop, c => light.Color = c);
                    break;
                }
                case 0xAFFF: // material
                {
                    var mat = new Material();
                    Materials.Add(mat);
                    Chunks(b, body, stop, mat);
                    break;
                }
                case 0xA000 when owner is Material mat:
                    mat.Name = CString(b, body, stop);
                    break;
                case 0xA010 when owner is Material mat:
                    ReadColor(b, body, stop, c => mat.Ambient = c);
                    break;
                case 0xA020 when owner is Material mat:
                    ReadColor(b, body, stop, c => mat.Diffuse = c);
                    break;
                case 0xA050 when owner is Material mat:
                    ReadPercent(b, body, stop, v => mat.Transparency = v);
                    break;
                case 0xA081 when owner is Material mat:
                    mat.TwoSided = true;
                    break;
                case 0xA200 when owner is Material mat: // texture map
                    Chunks(b, body, stop, mat);
                    break;
                case 0xA300 when owner is Material mat:
                    mat.Texture = CString(b, body, stop);
                    break;
                case 0xA354 when owner is Material mat: mat.UScale = F(b, body); break;
                case 0xA356 when owner is Material mat: mat.VScale = F(b, body); break;
                case 0xA358 when owner is Material mat: mat.UOffset = F(b, body); break;
                case 0xA35A when owner is Material mat: mat.VOffset = F(b, body); break;
            }
            p = stop;
        }
    }

    static float F(byte[] b, int at) => BitConverter.ToSingle(b, at);

    static string CString(byte[] b, int start, int stop)
    {
        int z = Array.IndexOf(b, (byte)0, start, stop - start);
        return Encoding.Latin1.GetString(b, start, (z < 0 ? stop : z) - start);
    }

    static void ReadColor(byte[] b, int start, int stop, Action<Color> set)
    {
        int p = start;
        while (p + 6 <= stop)
        {
            int id = BitConverter.ToUInt16(b, p);
            int len = BitConverter.ToInt32(b, p + 2);
            if (len < 6 || p + len > stop) break;
            if (id is 0x0011 or 0x0012) { set(Color.Color8(b[p + 6], b[p + 7], b[p + 8])); return; }
            if (id is 0x0010 or 0x0013) { set(new Color(F(b, p + 6), F(b, p + 10), F(b, p + 14))); return; }
            p += len;
        }
    }

    static void ReadPercent(byte[] b, int start, int stop, Action<float> set)
    {
        if (start + 6 > stop) return;
        int id = BitConverter.ToUInt16(b, start);
        if (id == 0x0030) set(BitConverter.ToUInt16(b, start + 6) / 100f);
        else if (id == 0x0031) set(F(b, start + 6));
    }

    // ---- building ----------------------------------------------------------------------------------------

    /// <summary>
    /// How the original's engine lights a scene's own meshes: per face, the colour numbers of the texture (or, without
    /// one, of the material) times ambient plus the light's diffuse, clamped at white; no doubling, and the material's
    /// colour is not used on a textured face. The light is fixed in the scene (towards it, in the world).
    /// </summary>
    public sealed record SceneLight(float Ambient, float Diffuse, Vector3 TowardsLight);

    static readonly Dictionary<(bool Alpha, bool TwoSided), Shader> sceneShaders = [];

    static Shader SceneShader(bool alpha, bool twoSided)
    {
        if (sceneShaders.TryGetValue((alpha, twoSided), out var s)) return s;
        string modes = $"unshaded, {(twoSided ? "cull_disabled" : "cull_back")}, {(alpha ? "blend_mix, depth_draw_never" : "depth_draw_opaque")}";
        s = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode {{modes}};
                uniform vec4 color = vec4(1.0);
                uniform sampler2D tex : repeat_enable, filter_linear_mipmap_anisotropic;
                uniform bool textured = false;
                uniform vec2 uv_scale = vec2(1.0);
                uniform vec2 uv_offset = vec2(0.0);
                uniform float ambient = 0.5;
                uniform float diffuse = 0.5;
                uniform vec3 towards_light = vec3(0.0, 1.0, 0.0);
                varying float lit;
                {{OrigLook.ColourSpace}}
                void vertex() {
                	vec3 n = normalize((MODEL_NORMAL_MATRIX * NORMAL));
                	lit = min(ambient + diffuse * max(dot(n, normalize(towards_light)), 0.0), 1.0);
                	UV = UV * uv_scale + uv_offset;
                }
                void fragment() {
                	vec4 c = textured ? texture(tex, UV) : color;
                	ALBEDO = to_linear(min(c.rgb * lit, vec3(1.0)));
                	{{(alpha ? "ALPHA = color.a * c.a;" : "")}}
                }
                """,
        };
        sceneShaders[(alpha, twoSided)] = s;
        return s;
    }

    /// <summary>
    /// Godot meshes for every mesh the filter accepts, placed by <paramref name="toWorld"/> (from the
    /// file's space). Textures come from <paramref name="texture"/> by the names in the file. With
    /// <paramref name="light"/> the meshes are lit as the original lit them (<see cref="SceneLight"/>).
    /// </summary>
    public Node3D Build(Func<Vector3, Vector3> toWorld, Func<string, Texture2D?> texture, Func<Mesh, bool>? include = null, bool mirrored = false, SceneLight? light = null)
    {
        var root = new Node3D { Name = "Scene3ds" };
        var mats = new Dictionary<string, Godot.Material>();
        Godot.Material MatFor(string? name)
        {
            name ??= "";
            if (mats.TryGetValue(name, out var known)) return known;
            var src = Materials.Find(x => x.Name == name);
            if (light is not null)
            {
                bool alpha = src is { Transparency: > 0.01f };
                var shader = new ShaderMaterial { Shader = SceneShader(alpha, src?.TwoSided == true) };
                var col = src?.Diffuse ?? new Color(0.7f, 0.7f, 0.7f);
                shader.SetShaderParameter("color", new Vector4(col.R, col.G, col.B, 1 - (src?.Transparency ?? 0)));
                shader.SetShaderParameter("ambient", light.Ambient);
                shader.SetShaderParameter("diffuse", light.Diffuse);
                shader.SetShaderParameter("towards_light", light.TowardsLight);
                if (src?.Texture is { } tname && texture(tname) is { } ttex)
                {
                    shader.SetShaderParameter("tex", ttex);
                    shader.SetShaderParameter("textured", true);
                    shader.SetShaderParameter("uv_scale", new Vector2(src.UScale, src.VScale));
                    shader.SetShaderParameter("uv_offset", new Vector2(src.UOffset, src.VOffset));
                }
                mats[name] = shader;
                return shader;
            }
            var sm = new StandardMaterial3D
            {
                AlbedoColor = src?.Diffuse ?? new Color(0.7f, 0.7f, 0.7f),
                Roughness = 1,
                CullMode = src?.TwoSided == true ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            if (src?.Texture is { } tn && texture(tn) is { } tex)
            {
                sm.AlbedoTexture = tex;
                sm.AlbedoColor = Colors.White;
                sm.Uv1Scale = new Vector3(src.UScale, src.VScale, 1);
                sm.Uv1Offset = new Vector3(src.UOffset, src.VOffset, 0);
            }
            if (src is { Transparency: > 0.01f })
            {
                sm.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                sm.AlbedoColor = sm.AlbedoColor with { A = 1 - src.Transparency };
            }
            mats[name] = sm;
            return sm;
        }
        foreach (var mesh in Meshes)
        {
            if (mesh.Faces.Length == 0 || (include is not null && !include(mesh))) continue;
            var am = new ArrayMesh();
            foreach (var group in Enumerable.Range(0, mesh.FaceMaterial.Length).GroupBy(f => mesh.FaceMaterial[f] ?? ""))
            {
                var st = new SurfaceTool();
                st.Begin(Godot.Mesh.PrimitiveType.Triangles);
                foreach (int f in group)
                {
                    int a = mesh.Faces[f * 3], b = mesh.Faces[f * 3 + 1], c = mesh.Faces[f * 3 + 2];
                    if (a >= mesh.Verts.Length || b >= mesh.Verts.Length || c >= mesh.Verts.Length) continue;
                    var pa = toWorld(mesh.Verts[a]);
                    var pb = toWorld(mesh.Verts[b]);
                    var pc = toWorld(mesh.Verts[c]);
                    // Godot's front faces wind clockwise; 3ds's are counter-clockwise (unless the space is mirrored).
                    int[] order = mirrored ? [a, b, c] : [a, c, b];
                    Vector3[] pos = mirrored ? [pa, pb, pc] : [pa, pc, pb];
                    var n = (pos[2] - pos[0]).Cross(pos[1] - pos[0]).Normalized();
                    for (int k = 0; k < 3; k++)
                    {
                        st.SetNormal(n);
                        if (mesh.Uvs is { } uv && order[k] < uv.Length) st.SetUV(new Vector2(uv[order[k]].X, 1 - uv[order[k]].Y));
                        st.AddVertex(pos[k]);
                    }
                }
                st.SetMaterial(MatFor(group.Key));
                st.Commit(am);
            }
            root.AddChild(new MeshInstance3D { Name = mesh.Name, Mesh = am });
        }
        return root;
    }
}
