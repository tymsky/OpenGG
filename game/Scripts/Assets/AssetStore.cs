using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenGG.Core.Content;

namespace OpenGG.Assets;

/// <summary>Something that can turn logical asset ids into loaded assets.</summary>
public interface IAssetSource
{
    ModelData? Model(string id);
    Texture2D? Texture(string id);
    /// <summary>UI pictures (icons, portraits) rendered at roughly <paramref name="size"/> pixels.</summary>
    Texture2D? Icon(string id, int size);
    AudioStream? Sound(string id);
}

/// <summary>
/// Loads models, textures, icons and sounds by logical id (e.g. "part.i4_head"). Sources are asked
/// in order; the first that knows the id wins. Results are cached.
/// </summary>
public sealed class AssetStore
{
    readonly List<IAssetSource> sources = [];
    readonly Dictionary<string, ModelData?> models = [];
    readonly Dictionary<string, Texture2D?> textures = [];
    readonly Dictionary<string, Texture2D?> icons = [];
    readonly Dictionary<string, AudioStream?> sounds = [];

    public void AddSource(IAssetSource source, bool first = false)
    {
        if (first) sources.Insert(0, source);
        else sources.Add(source);
    }

    /// <summary>Changes the materials of every model loaded from now on (OpenGG's own look restyles its placeholders).</summary>
    public Func<Material?, Material?>? Styler { get; set; }

    public ModelData? Model(string id) => Cached(models, id, s => Styled(s.Model(id)));

    ModelData? Styled(ModelData? data)
    {
        if (data is null || Styler is null) return data;
        foreach (var p in data.Pieces)
            if (p.Mesh is ArrayMesh am)
                for (int i = 0; i < am.GetSurfaceCount(); i++) am.SurfaceSetMaterial(i, Styler(am.SurfaceGetMaterial(i)));
        return data;
    }
    public Texture2D? Texture(string id) => Cached(textures, id, s => s.Texture(id));
    public Texture2D? Icon(string id, int size = 48) => Cached(icons, $"{id}@{size}", s => s.Icon(id, size));
    public AudioStream? Sound(string id) => Cached(sounds, id, s => s.Sound(id));

    T? Cached<T>(Dictionary<string, T?> cache, string key, Func<IAssetSource, T?> load) where T : class
    {
        if (cache.TryGetValue(key, out var hit)) return hit;
        T? found = null;
        foreach (var s in sources)
        {
            try
            {
                found = load(s);
            }
            catch (Exception e)
            {
                GD.PushWarning($"Asset {key}: {e.Message}");
            }
            if (found is not null) break;
        }
        if (found is null) GD.PushWarning($"Missing asset {key}");
        cache[key] = found;
        return found;
    }

    // ---- helpers shared by sources ------------------------------------------------------

    public static Texture2D? SvgTexture(byte[] svg, int size)
    {
        var img = new Image();
        if (img.LoadSvgFromBuffer(svg, 1f) != Error.Ok) return null;
        int w = Math.Max(img.GetWidth(), img.GetHeight());
        if (w > 0 && w != size)
        {
            img = new Image();
            if (img.LoadSvgFromBuffer(svg, size / (float)w) != Error.Ok) return null;
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static Texture2D? ImageFileTexture(byte[] bytes, string ext, bool mipmaps = true)
    {
        var img = new Image();
        var err = ext switch
        {
            ".png" => img.LoadPngFromBuffer(bytes),
            ".jpg" or ".jpeg" => img.LoadJpgFromBuffer(bytes),
            ".tga" => img.LoadTgaFromBuffer(bytes),
            ".bmp" => img.LoadBmpFromBuffer(bytes),
            ".webp" => img.LoadWebpFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        if (err != Error.Ok) return null;
        if (mipmaps) img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}

/// <summary>A content pack described by a manifest on disk (the bundled "ai" pack).</summary>
public sealed class ManifestSource(string dir, AssetManifest manifest) : IAssetSource
{
    public string Dir { get; } = dir;
    public AssetManifest Manifest { get; } = manifest;

    string? PathOf(Dictionary<string, string> map, string id) =>
        map.TryGetValue(id, out var file) ? System.IO.Path.Combine(Dir, file) : null;

    public ModelData? Model(string id)
    {
        if (PathOf(Manifest.Models, id) is not { } path || !File.Exists(path)) return null;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok) return null;
        var scene = doc.GenerateScene(state);
        if (scene is null) return null;
        var data = new ModelData();
        Collect(scene, Transform3D.Identity, data, isRoot: true);
        scene.Free();
        return data;
    }

    static void Collect(Node node, Transform3D parent, ModelData data, bool isRoot)
    {
        var xf = parent;
        if (node is Node3D n3 && !isRoot) xf = parent * n3.Transform;
        if (node is MeshInstance3D mi && mi.Mesh is not null) data.Add(mi.Mesh, xf);
        foreach (var child in node.GetChildren()) Collect(child, xf, data, isRoot: false);
    }

    public Texture2D? Texture(string id)
    {
        if (PathOf(Manifest.Textures, id) is not { } path || !File.Exists(path)) return null;
        return AssetStore.ImageFileTexture(File.ReadAllBytes(path), System.IO.Path.GetExtension(path).ToLowerInvariant());
    }

    public Texture2D? Icon(string id, int size)
    {
        var path = PathOf(Manifest.Icons, id) ?? PathOf(Manifest.Textures, id);
        if (path is null || !File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? AssetStore.SvgTexture(bytes, size)
            : AssetStore.ImageFileTexture(bytes, System.IO.Path.GetExtension(path).ToLowerInvariant(), mipmaps: false);
    }

    public AudioStream? Sound(string id)
    {
        if (PathOf(Manifest.Sounds, id) is not { } path || !File.Exists(path)) return null;
        return Wav.Load(File.ReadAllBytes(path));
    }
}
