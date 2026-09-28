using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenGG.Assets;
using CoreJson = OpenGG.Core.Content.Json;

namespace OpenGG.Ui;

/// <summary>
/// An optional skin: screens, pictures, bitmap fonts, cursors and sounds. Either the original game's own look, read in
/// memory from the player's copy of the game (<see cref="LoadOriginal"/>), or a folder the player points OpenGG at (a
/// <c>skin.json</c> in it; one that says <c>"extends": "original"</c> changes the original's look). Without one
/// everything is drawn in code.
/// <para>A screen is three full-size pictures: the screen with nothing on it, with every button up, and
/// with every button down. The screen shows the first; each button cuts its own place out of the second
/// or the third.</para>
/// </summary>
public static class UiSkin
{
    public sealed class Manifest
    {
        /// <summary>Where the files are, relative to the skin folder (or absolute).</summary>
        public string? Root { get; set; }
        /// <summary>"original": this skin changes the original game's look (its files are looked for in the skin's folder
        /// first, then in the game's archives).</summary>
        public string? Extends { get; set; }
        public Dictionary<string, ScreenSpec> Screens { get; set; } = [];
        public Dictionary<string, PictureSpec> Pictures { get; set; } = [];
        public Dictionary<string, FontSpec> Fonts { get; set; } = [];
        public Dictionary<string, CursorSpec> Cursors { get; set; } = [];
        /// <summary>Sound ids (as the game plays them) to files.</summary>
        public Dictionary<string, string> Sounds { get; set; } = [];
        /// <summary>3D scenes (.3ds files, their textures next to them) by the game's scene id.</summary>
        public Dictionary<string, string> Scenes { get; set; } = [];
        /// <summary>Words for Free Play's random jobs: a phrase file in the original's format
        /// (<see cref="Core.Original.PhraseFile"/>), the customers' faces next to it.</summary>
        public string? JobPhrases { get; set; }
        /// <summary>The game's sentences (dialogs, hints, messages) by their key in <see cref="Words"/>, in place of
        /// OpenGG's own; {name} marks what goes in.</summary>
        public Dictionary<string, string> Texts { get; set; } = [];
    }

    public sealed class ScreenSpec
    {
        public string? Base { get; set; }
        public string? Up { get; set; }
        public string? Down { get; set; }
    }

    public sealed class PictureSpec
    {
        public string File { get; set; } = "";
        /// <summary>Colour that is see-through (#rrggbb), and how far a pixel may be from it.</summary>
        public string? Key { get; set; }
        public int Tolerance { get; set; }
        /// <summary>Part of the file: x, y, w, h.</summary>
        public int[]? Rect { get; set; }
    }

    public sealed class FontSpec
    {
        public string File { get; set; } = "";
        public string? Key { get; set; }
        public int Tolerance { get; set; }
        /// <summary>Pixels from the top of a glyph to the baseline.</summary>
        public int Ascent { get; set; }
        /// <summary>Extra pixels between letters, and the width of a space.</summary>
        public int Spacing { get; set; } = 1;
        public int Space { get; set; } = 4;
        /// <summary>Each character's place in the file: x, y, w, h.</summary>
        public Dictionary<string, int[]> Glyphs { get; set; } = [];
        /// <summary>Where the next character goes after this one, where measured (else its width and the spacing).</summary>
        public Dictionary<string, int> Advances { get; set; } = [];
    }

    public sealed class CursorSpec
    {
        public string File { get; set; } = "";
        public string? Key { get; set; }
        public int Tolerance { get; set; }
        public int[] Hotspot { get; set; } = [0, 0];
    }

    public sealed record ScreenPictures(Texture2D? Base, Texture2D? Up, Texture2D? Down);

    static Manifest? manifest;
    static SkinFiles files = new FolderFiles("");
    static readonly Dictionary<string, Image?> images = [];
    static readonly Dictionary<string, Texture2D?> pictures = [];
    static readonly Dictionary<string, ScreenPictures> screens = [];
    static readonly Dictionary<string, Font?> fonts = [];
    static readonly Dictionary<string, AudioStream?> sounds = [];
    static readonly Dictionary<string, View3D.Model3ds?> scenes = [];

    public static bool Active => manifest is not null;

    /// <summary>Is the look the original's own (not a skin folder's)?</summary>
    public static bool IsOriginal { get; private set; }

    /// <summary>The original adds its overlays (hints, tags, labels) onto what is under them.</summary>
    public static readonly CanvasItemMaterial Additive = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    static void Clear()
    {
        manifest = null;
        IsOriginal = false;
        images.Clear();
        pictures.Clear();
        screens.Clear();
        fonts.Clear();
        sounds.Clear();
        scenes.Clear();
    }

    /// <summary>Reads the skin in <paramref name="folder"/>; false (and no skin) if there is none. A skin that extends the
    /// original's look needs the game's <paramref name="archives"/>.</summary>
    public static bool Load(string? folder, Core.Original.OriginalArchives? archives = null)
    {
        Clear();
        if (string.IsNullOrEmpty(folder)) return false;
        var file = Path.Combine(folder, "skin.json");
        if (!File.Exists(file)) return false;
        try
        {
            var m = CoreJson.Parse<Manifest>(File.ReadAllText(file));
            var root = m.Root is { } r ? Path.GetFullPath(Path.Combine(folder, r)) : folder;
            if (m.Extends == "original")
            {
                if (archives is null || OriginalManifest() is not { } baseLook) return false;
                manifest = Merge(baseLook, m);
                files = new LayeredFiles(new FolderFiles(root), new ArchiveFiles(archives));
            }
            else
            {
                manifest = m;
                files = new FolderFiles(root);
            }
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"skin: {e.Message}");
            manifest = null;
            return false;
        }
    }

    /// <summary>The original game's look, from the player's own copy (its archives, read in memory).</summary>
    public static bool LoadOriginal(Core.Original.OriginalArchives archives)
    {
        Clear();
        if (OriginalManifest() is not { } m) return false;
        manifest = m;
        files = new ArchiveFiles(archives);
        IsOriginal = true;
        return true;
    }

    /// <summary>Which of the original's files make up its look (Skins/original.json, built into the game).</summary>
    static Manifest? OriginalManifest()
    {
        using var stream = typeof(UiSkin).Assembly.GetManifestResourceStream("OpenGG.Skins.original.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return CoreJson.Parse<Manifest>(reader.ReadToEnd());
    }

    /// <summary>A skin's entries over another's.</summary>
    static Manifest Merge(Manifest under, Manifest over)
    {
        static Dictionary<string, T> Over<T>(Dictionary<string, T> a, Dictionary<string, T> b)
        {
            var d = new Dictionary<string, T>(a);
            foreach (var (k, v) in b) d[k] = v;
            return d;
        }
        return new Manifest
        {
            Screens = Over(under.Screens, over.Screens),
            Pictures = Over(under.Pictures, over.Pictures),
            Fonts = Over(under.Fonts, over.Fonts),
            Cursors = Over(under.Cursors, over.Cursors),
            Sounds = Over(under.Sounds, over.Sounds),
            Scenes = Over(under.Scenes, over.Scenes),
            JobPhrases = over.JobPhrases ?? under.JobPhrases,
            Texts = Over(under.Texts, over.Texts),
        };
    }

    // ---- pictures -----------------------------------------------------------------------------------

    static Image? LoadImage(string rel)
    {
        if (images.TryGetValue(rel, out var cached)) return cached;
        Image? img = null;
        if (files.Read(rel) is { } bytes)
        {
            img = new Image();
            var err = Path.GetExtension(rel).ToLowerInvariant() switch
            {
                ".tga" => img.LoadTgaFromBuffer(bytes),
                ".png" => img.LoadPngFromBuffer(bytes),
                ".bmp" => img.LoadBmpFromBuffer(bytes),
                ".jpg" or ".jpeg" => img.LoadJpgFromBuffer(bytes),
                _ => Error.FileUnrecognized,
            };
            if (err != Error.Ok)
            {
                GD.PushWarning($"skin: can't read {rel}");
                img = null;
            }
            else img.Convert(Image.Format.Rgba8);
        }
        else GD.PushWarning($"skin: {rel} is missing");
        images[rel] = img;
        return img;
    }

    /// <summary>A copy of the picture with the key colour made see-through.</summary>
    static Image Keyed(Image src, string? key, int tolerance, int[]? rect = null)
    {
        var img = rect is { Length: 4 } r ? src.GetRegion(new Rect2I(r[0], r[1], r[2], r[3])) : (Image)src.Duplicate();
        if (key is null) return img;
        var k = Color.FromHtml(key);
        int kr = (int)(k.R * 255), kg = (int)(k.G * 255), kb = (int)(k.B * 255);
        var data = img.GetData();
        for (int i = 0; i < data.Length; i += 4)
            if (Math.Abs(data[i] - kr) <= tolerance && Math.Abs(data[i + 1] - kg) <= tolerance && Math.Abs(data[i + 2] - kb) <= tolerance)
                data[i + 3] = 0;
        return Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
    }

    static Texture2D? Texture(string? rel)
    {
        if (rel is null) return null;
        if (pictures.TryGetValue("file:" + rel, out var t)) return t;
        var img = LoadImage(rel);
        t = img is null ? null : ImageTexture.CreateFromImage(img);
        pictures["file:" + rel] = t;
        return t;
    }

    /// <summary>The three pictures of a screen, if the skin has it.</summary>
    public static ScreenPictures? Screen(string id)
    {
        if (manifest is null || !manifest.Screens.TryGetValue(id, out var s)) return null;
        if (!screens.TryGetValue(id, out var p))
        {
            p = new ScreenPictures(Texture(s.Base), Texture(s.Up ?? s.Base), Texture(s.Down ?? s.Up ?? s.Base));
            screens[id] = p;
        }
        return p;
    }

    public static Texture2D? Picture(string id)
    {
        if (manifest is null || !manifest.Pictures.TryGetValue(id, out var s)) return null;
        if (pictures.TryGetValue(id, out var t)) return t;
        var img = LoadImage(s.File);
        t = img is null ? null : ImageTexture.CreateFromImage(Keyed(img, s.Key, s.Tolerance, s.Rect));
        pictures[id] = t;
        return t;
    }

    public static bool Has(string pictureId) => manifest?.Pictures.ContainsKey(pictureId) == true;

    /// <summary>The skin's words for a sentence of the game, if it has them (see <see cref="Words"/>).</summary>
    public static string? Text(string key) => manifest?.Texts.GetValueOrDefault(key);

    // ---- fonts ----------------------------------------------------------------------------------------

    /// <summary>A bitmap font cut from a strip of letters. Draw it at <see cref="FontSize"/>.</summary>
    public static Font? Font(string id)
    {
        if (manifest is null || !manifest.Fonts.TryGetValue(id, out var s)) return null;
        if (fonts.TryGetValue(id, out var f)) return f;
        f = null;
        if (LoadImage(s.File) is { } src)
        {
            var img = Keyed(src, s.Key, s.Tolerance);
            int h = 0;
            foreach (var g in s.Glyphs.Values) h = Math.Max(h, g[3]);
            var ff = new FontFile
            {
                FixedSize = h,
                Antialiasing = TextServer.FontAntialiasing.None,
                SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
                Hinting = TextServer.Hinting.None,
                GenerateMipmaps = false,
            };
            var size = new Vector2I(h, 0);
            ff.SetTextureImage(0, size, 0, img);
            int ascent = s.Ascent > 0 ? s.Ascent : h;
            ff.SetCacheAscent(0, h, ascent);
            ff.SetCacheDescent(0, h, h - ascent);
            foreach (var (key, g) in s.Glyphs)
            {
                if (key.Length != 1) continue;
                int ch = key[0];
                ff.SetGlyphTextureIdx(0, size, ch, 0);
                ff.SetGlyphUVRect(0, size, ch, new Rect2(g[0], g[1], g[2], g[3]));
                ff.SetGlyphSize(0, size, ch, new Vector2(g[2], g[3]));
                ff.SetGlyphOffset(0, size, ch, new Vector2(0, -ascent));
                ff.SetGlyphAdvance(0, h, ch, new Vector2(s.Advances.TryGetValue(key, out var advance) ? advance : g[2] + s.Spacing, 0));
            }
            ff.SetGlyphAdvance(0, h, ' ', new Vector2(s.Space, 0));
            ff.SetGlyphSize(0, size, ' ', Vector2.Zero);
            f = ff;
        }
        fonts[id] = f;
        return f;
    }

    /// <summary>The size a skin font is drawn at (its own pixel height).</summary>
    public static int FontSize(Font f) => f is FontFile { FixedSize: > 0 } ff ? ff.FixedSize : 16;

    // ---- cursors and sounds --------------------------------------------------------------------------

    /// <summary>The skin's cursor <paramref name="id"/> as a picture at its own size, and its hot spot.</summary>
    public static (Image Picture, Vector2I Hotspot)? CursorPicture(string id)
    {
        if (manifest is null || !manifest.Cursors.TryGetValue(id, out var s) || LoadImage(s.File) is not { } src) return null;
        return (Keyed(src, s.Key, s.Tolerance), new Vector2I(s.Hotspot[0], s.Hotspot[1]));
    }

    /// <summary>Sets the mouse pointer to the skin's cursor <paramref name="id"/>, scaled like the screen.</summary>
    public static bool SetCursor(string id, float scale)
    {
        if (manifest is null || !manifest.Cursors.TryGetValue(id, out var s) || LoadImage(s.File) is not { } src) return false;
        var img = Keyed(src, s.Key, s.Tolerance);
        int k = Math.Max(1, (int)MathF.Round(scale));
        if (k > 1) img.Resize(img.GetWidth() * k, img.GetHeight() * k, Image.Interpolation.Nearest);
        Input.SetCustomMouseCursor(ImageTexture.CreateFromImage(img), Input.CursorShape.Arrow, new Vector2(s.Hotspot[0], s.Hotspot[1]) * k);
        Input.SetCustomMouseCursor(ImageTexture.CreateFromImage(img), Input.CursorShape.PointingHand, new Vector2(s.Hotspot[0], s.Hotspot[1]) * k);
        return true;
    }

    /// <summary>The skin's sound for <paramref name="id"/>; null when it has none, or names none (an empty name:
    /// the skin's game is silent there, see <see cref="HasSound"/>).</summary>
    public static AudioStream? Sound(string id)
    {
        if (manifest is null || !manifest.Sounds.TryGetValue(id, out var rel) || rel.Length == 0) return null;
        if (sounds.TryGetValue(id, out var s)) return s;
        s = files.Read(rel) is { } bytes ? Wav.Load(bytes) : null;
        sounds[id] = s;
        return s;
    }

    /// <summary>The skin says what plays for <paramref name="id"/>, a sound or (an empty name) nothing.</summary>
    public static bool HasSound(string id) => manifest?.Sounds.ContainsKey(id) == true;

    // ---- files for the game's content ----------------------------------------------------------------------

    /// <summary>The skin's phrase file for random jobs (a path relative to the skin's files), if it has one.</summary>
    public static string? JobPhrases => manifest?.JobPhrases;

    /// <summary>A file of the skin by its relative path (found in any letter case), or null.</summary>
    public static byte[]? ReadFile(string rel) => manifest is null ? null : files.Read(rel);

    /// <summary>A picture file of the skin at about <paramref name="size"/> pixels (portraits).</summary>
    public static Texture2D? FilePicture(string rel, int size)
    {
        if (manifest is null || files.Find(rel) is not { } found || LoadImage(found) is not { } img) return null;
        var copy = (Image)img.Duplicate();
        float k = size / (float)Math.Max(copy.GetWidth(), copy.GetHeight());
        if (Math.Abs(k - 1) > 0.01f) copy.Resize(Math.Max(1, (int)(copy.GetWidth() * k)), Math.Max(1, (int)(copy.GetHeight() * k)), Image.Interpolation.Bilinear);
        return ImageTexture.CreateFromImage(copy);
    }

    // ---- 3D scenes ----------------------------------------------------------------------------------------

    /// <summary>The skin's 3D scene <paramref name="id"/> (a .3ds file), if it has one.</summary>
    public static View3D.Model3ds? Scene(string id)
    {
        if (manifest is null || !manifest.Scenes.TryGetValue(id, out var rel)) return null;
        if (scenes.TryGetValue(id, out var m)) return m;
        try { m = files.Read(rel) is { } bytes ? View3D.Model3ds.Read(bytes) : null; }
        catch (Exception e)
        {
            GD.PushWarning($"skin: {rel}: {e.Message}");
            m = null;
        }
        scenes[id] = m;
        return m;
    }

    /// <summary>A texture a scene names, looked up next to its file (in any letter case).</summary>
    public static Texture2D? SceneTexture(string sceneId, string name)
    {
        if (manifest is null || !manifest.Scenes.TryGetValue(sceneId, out var rel)) return null;
        var dir = Path.GetDirectoryName(rel) ?? "";
        var key = "scene:" + Path.Combine(dir, name).ToLowerInvariant();
        if (pictures.TryGetValue(key, out var t)) return t;
        string? found = files.Find(Path.Combine(dir, name));
        t = null;
        if (found is not null && LoadImage(found) is { } img)
        {
            var copy = (Image)img.Duplicate();
            copy.GenerateMipmaps();
            t = ImageTexture.CreateFromImage(copy);
        }
        pictures[key] = t;
        return t;
    }

    // ---- drawing -------------------------------------------------------------------------------------

    /// <summary>Draws the part of a full-screen picture that lies under <paramref name="c"/>
    /// (grown by <paramref name="grow"/> pixels to take in the edges of pressed plates).</summary>
    public static void DrawUnder(CanvasItem c, Texture2D tex, Rect2 local, int grow = 0)
    {
        var at = c.GetGlobalTransform() * local.Position;
        var src = new Rect2(at, local.Size).Grow(grow);
        var dst = local.Grow(grow);
        // Keep inside the picture.
        var clip = src.Intersection(new Rect2(Vector2.Zero, tex.GetSize()));
        if (clip.Size.X <= 0 || clip.Size.Y <= 0) return;
        dst = new Rect2(dst.Position + (clip.Position - src.Position), clip.Size);
        c.DrawTextureRectRegion(tex, dst, clip);
    }
}

/// <summary>Content pictures a skin brings: "skin:&lt;path&gt;" (relative to the skin's files, any letter case),
/// e.g. the faces of the random jobs' customers.</summary>
public sealed class SkinSource : Assets.IAssetSource
{
    public Assets.ModelData? Model(string id) => null;
    public Texture2D? Texture(string id) => id.StartsWith("skin:", StringComparison.Ordinal) ? UiSkin.FilePicture(id[5..], 256) : null;
    public Texture2D? Icon(string id, int size) => id.StartsWith("skin:", StringComparison.Ordinal) ? UiSkin.FilePicture(id[5..], size) : null;
    public AudioStream? Sound(string id) => null;
}
