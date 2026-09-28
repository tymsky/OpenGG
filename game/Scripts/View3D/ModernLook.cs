using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace OpenGG.View3D;

/// <summary>
/// OpenGG's own look (no skin): a modern, clean and lightly stylised take on the original's 3D. The 3D is drawn at the
/// screen's own resolution; the WorkShop is lit like a studio (a key light from over the viewer's left shoulder as in
/// the original, soft shadows, a dark studio's softboxes in the reflections, ambient occlusion), the JunkYard, the Car
/// Lot and the Auction by daylight on the HD pack's CC0 surfaces; the placeholder models' materials get car paint with a
/// clear coat, mirror chrome, clear glass and a little grain on the metals and the rubber, and damaged parts rusty bare
/// metal in their condition's colour. The original's look (a skin) is not touched by any of this.
/// </summary>
public static class ModernLook
{
    /// <summary>OpenGG's own look is on (set once the skin is known).</summary>
    public static bool On { get; set; }

    /// <summary>The renderer's quality for the look: soft shadows with enough samples not to show their noise, and
    /// ambient occlusion at the full resolution.</summary>
    public static void Setup()
    {
        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.High, false, 0.5f, 2, 50, 300);
    }

    /// <summary>How many screen pixels one of the game's pixels can take on this screen, full screen: pictures made for
    /// the look (icons, the parts' thumbnails) are made this much bigger, so they stay sharp.</summary>
    public static int DetailScale
    {
        get
        {
            var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
            float k = Math.Min(screen.X / (float)ScreenMode.Width, screen.Y / (float)ScreenMode.Height);
            return Math.Clamp((int)MathF.Ceiling(k - 0.01f), 1, 4);
        }
    }

    // ---- materials -------------------------------------------------------------------------------------------

    static readonly Dictionary<string, Material> styled = [];

    /// <summary>A placeholder model's material in the modern look, by its name (the generator's material library).</summary>
    public static Material? Style(Material? m)
    {
        if (m is not StandardMaterial3D s || s.ResourceName is not { Length: > 0 } name) return m;
        if (styled.TryGetValue(name, out var done)) return done;
        var c = (StandardMaterial3D)s.Duplicate();
        switch (name)
        {
            case "paint" or "red_coat" or "blue_coat" or "yellow_coat" or "orange_coat" or "green_coat" or "tool_red" or "lift_yellow":
                // Car paint: a clear coat over the colour, with the faint ripple of sprayed paint ("orange peel") that
                // keeps its reflections from looking like glass.
                // Solid paint: the colour coat itself is dull and barely metallic; the shine is the clear coat's.
                c.ClearcoatEnabled = true;
                c.Clearcoat = 1f;
                c.ClearcoatRoughness = 0.06f;
                c.Roughness = 0.5f;
                c.Metallic = Mathf.Min(c.Metallic, 0.12f);
                c.NormalEnabled = true;
                c.NormalTexture = Grain(normal: true, frequency: 0.05f);
                c.NormalScale = 0.05f;
                Triplanar(c, 6f);
                break;
            case "plastic_black" or "trim_black":
                // Moulded plastic: a fine grain, satin.
                c.Roughness = Mathf.Max(c.Roughness, 0.58f);
                c.NormalEnabled = true;
                c.NormalTexture = Grain(normal: true, frequency: 0.12f);
                c.NormalScale = 0.3f;
                Triplanar(c, 12f);
                break;
            case "seal":
                c.Roughness = 0.85f;
                c.NormalEnabled = true;
                c.NormalTexture = Grain(normal: true, frequency: 0.15f);
                c.NormalScale = 0.25f;
                Triplanar(c, 14f);
                break;
            case "fabric":
                // Upholstery: a woven texture, no shine.
                c.Roughness = 1f;
                c.NormalEnabled = true;
                c.NormalTexture = Grain(normal: true, frequency: 0.3f);
                c.NormalScale = 0.45f;
                Triplanar(c, 20f);
                break;
            case "glass_tint":
                c.Roughness = 0.02f;
                c.MetallicSpecular = 0.5f;
                break;
            case "lens_clear" or "lens_red" or "lens_amber":
            {
                // Lamp lenses: clear or coloured plastic over the reflectors, reflecting as the glass does (premultiplied,
                // so the reflections stay whole); the coloured ones let through less of what is behind them.
                float a = name == "lens_clear" ? 0.06f : 0.3f;
                var lin = s.AlbedoColor.SrgbToLinear();
                var pre = new Color(lin.R * a, lin.G * a, lin.B * a).LinearToSrgb();
                c.AlbedoColor = new Color(pre.R, pre.G, pre.B, a);
                c.Roughness = 0.03f;
                c.MetallicSpecular = 0.6f;
                c.BlendMode = BaseMaterial3D.BlendModeEnum.PremultAlpha;
                break;
            }
            case "chrome":
                c.Metallic = 1f;
                c.Roughness = 0.035f;
                break;
            case "glass":
                // Clear, faintly green glass: little of its own colour, its reflections whole (premultiplied alpha
                // keeps them from fading with the colour), so what is behind it shows.
                c.Roughness = 0.02f;
                c.MetallicSpecular = 0.5f;
                // Premultiplied: the colour given is already multiplied by the alpha (0.12).
                c.AlbedoColor = new Color(0.066f, 0.08f, 0.074f, 0.12f);
                c.BlendMode = BaseMaterial3D.BlendModeEnum.PremultAlpha;
                break;
            case "rubber" or "friction":
                c.Roughness = 0.86f;
                c.NormalEnabled = true;
                c.NormalTexture = Grain(normal: true, frequency: 0.35f);
                c.NormalScale = 0.35f;
                Triplanar(c, 6f);
                break;
            case "steel" or "dark_steel" or "cast_iron" or "aluminum" or "exhaust_steel" or "rust" or "copper" or "brass" or "radiator_core" or "underbody":
                // Metals: the roughness a little uneven, as worked metal is.
                c.RoughnessTexture = Grain(normal: false, frequency: 0.08f);
                c.Roughness = Mathf.Min(1f, c.Roughness * 1.15f);
                Triplanar(c, 2.5f);
                break;
            case "headlight" or "taillight" or "amber_light":
                c.EmissionEnergyMultiplier = 1.6f;
                break;
            case "wood" when Surface("Planks037A", 1.2f, mean: s.AlbedoColor, triplanar: true) is { } planks:
                // Boards, in the placeholders' own brown.
                planks.ResourceName = name;
                styled[name] = planks;
                return planks;
        }
        styled[name] = c;
        return c;
    }

    static void Triplanar(StandardMaterial3D c, float scale)
    {
        c.Uv1Triplanar = true;
        c.Uv1Scale = new Vector3(scale, scale, scale);
    }

    static readonly Dictionary<(bool, float), NoiseTexture2D> grains = [];

    /// <summary>A seamless grain: a normal map (rubber) or a roughness map between 0.8 and 1 (metals).</summary>
    static NoiseTexture2D Grain(bool normal, float frequency)
    {
        if (grains.TryGetValue((normal, frequency), out var t)) return t;
        t = new NoiseTexture2D
        {
            Width = 512,
            Height = 512,
            Seamless = true,
            AsNormalMap = normal,
            BumpStrength = 4f,
            GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = frequency, FractalOctaves = 4 },
        };
        if (!normal)
        {
            var ramp = new Gradient();
            ramp.SetColor(0, new Color(0.8f, 0.8f, 0.8f));
            ramp.SetColor(1, Colors.White);
            t.ColorRamp = ramp;
        }
        grains[(normal, frequency)] = t;
        return t;
    }

    // ---- the HD pack (data/hd: CC0 pictures, listed in its LICENSES.md) --------------------------------------

    static readonly Dictionary<string, Texture2D?> hd = [];

    static Image? LoadHd(string rel)
    {
        var path = Path.Combine(Paths.HdPack, rel);
        return File.Exists(path) ? Image.LoadFromFile(path) : null;
    }

    static Texture2D? HdCached(string key, Func<Image?> make)
    {
        if (hd.TryGetValue(key, out var t)) return t;
        var img = make();
        t = img is null ? null : ImageTexture.CreateFromImage(img);
        hd[key] = t;
        return t;
    }

    /// <summary>A picture of the HD pack, with its mipmaps (a normal map's kept normalised); null without the pack.</summary>
    static Texture2D? Hd(string rel, bool normal = false) => HdCached(rel, () =>
    {
        var img = LoadHd(rel);
        img?.GenerateMipmaps(normal);
        return img;
    });

    static readonly float[] toLinear = BuildToLinear();

    static float[] BuildToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float c = i / 255f;
            t[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return t;
    }

    static float ToSrgb(float l) => l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1 / 2.4f) - 0.055f;

    static byte ToSrgbByte(float l) => (byte)Math.Clamp(MathF.Round(ToSrgb(Math.Max(0, l)) * 255), 0, 255);

    /// <summary>
    /// A colour picture of the HD pack graded to <paramref name="mean"/> (as a material's colour would be given), its
    /// detail kept: the pack's photographs come out in the placeholders' own palette.
    /// </summary>
    static Texture2D? HdGraded(string rel, Color mean) => HdCached($"{rel}|{mean.ToHtml()}", () =>
    {
        if (LoadHd(rel) is not { } img) return null;
        img.Convert(Image.Format.Rgb8);
        var d = img.GetData();
        int n = d.Length / 3;
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < d.Length; i += 3)
        {
            r += toLinear[d[i]];
            g += toLinear[d[i + 1]];
            b += toLinear[d[i + 2]];
        }
        var want = mean.SrgbToLinear();
        float gr = want.R / (float)(r / n), gg = want.G / (float)(g / n), gb = want.B / (float)(b / n);
        for (int i = 0; i < d.Length; i += 3)
        {
            d[i] = ToSrgbByte(toLinear[d[i]] * gr);
            d[i + 1] = ToSrgbByte(toLinear[d[i + 1]] * gg);
            d[i + 2] = ToSrgbByte(toLinear[d[i + 2]] * gb);
        }
        var graded = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgb8, d);
        graded.GenerateMipmaps();
        return graded;
    });

    /// <summary>A colour picture with its cut-out (a separate opacity picture) as its alpha, the cut-out's solid
    /// parts widened by <paramref name="widen"/> pixels (a wire mesh stays visible from further off).</summary>
    static Texture2D? HdCutOut(string colour, string opacity, int widen = 0) => HdCached($"{colour}+{opacity}+{widen}", () =>
    {
        if (LoadHd(colour) is not { } c || LoadHd(opacity) is not { } a) return null;
        c.Convert(Image.Format.Rgb8);
        a.Convert(Image.Format.L8);
        var cd = c.GetData();
        var ad = a.GetData();
        int w = a.GetWidth(), h = a.GetHeight();
        for (int pass = 0; pass < widen; pass++)
        {
            // The brightest of each pixel's neighbours (the picture tiles, so they wrap round).
            var wider = new byte[ad.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte m = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            m = Math.Max(m, ad[(y + dy + h) % h * w + (x + dx + w) % w]);
                    wider[y * w + x] = m;
                }
            ad = wider;
        }
        var rgba = new byte[ad.Length * 4];
        for (int i = 0; i < ad.Length; i++)
        {
            rgba[i * 4] = cd[i * 3];
            rgba[i * 4 + 1] = cd[i * 3 + 1];
            rgba[i * 4 + 2] = cd[i * 3 + 2];
            rgba[i * 4 + 3] = ad[i];
        }
        var img = Image.CreateFromData(c.GetWidth(), c.GetHeight(), false, Image.Format.Rgba8, rgba);
        img.GenerateMipmaps();
        return img;
    });

    /// <summary>
    /// A surface of the HD pack: its colour, bumps and roughness, one tile every <paramref name="tile"/> metres of a
    /// plane <paramref name="size"/> big (a PlaneMesh's own UVs), or all round a model's own space with
    /// <paramref name="triplanar"/>. <paramref name="mean"/> grades its colour to the placeholders' palette. Null
    /// without the pack.
    /// </summary>
    public static StandardMaterial3D? Surface(string asset, float tile, Vector2 size = default, Color? mean = null, bool triplanar = false, float bumps = 1f)
    {
        var colour = mean is { } m ? HdGraded($"textures/{asset}/color.jpg", m) : Hd($"textures/{asset}/color.jpg");
        if (colour is null) return null;
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = colour,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 1f,
        };
        if (Hd($"textures/{asset}/normal.jpg", normal: true) is { } normal)
        {
            mat.NormalEnabled = true;
            mat.NormalTexture = normal;
            mat.NormalScale = bumps;
        }
        if (Hd($"textures/{asset}/roughness.jpg") is { } rough) mat.RoughnessTexture = rough;
        if (triplanar)
        {
            mat.Uv1Triplanar = true;
            mat.Uv1Scale = Vector3.One / tile;
        }
        else mat.Uv1Scale = new Vector3(size.X / tile, size.Y / tile, 1);
        return mat;
    }

    /// <summary>A chain-link fence's wire mesh, one tile every <paramref name="tile"/> metres of a
    /// <paramref name="size"/> panel; far off it fades to a haze, as a real one does. Null without the pack.</summary>
    public static StandardMaterial3D? ChainLink(Vector2 size, float tile)
    {
        // The wires two pixels thicker all round: in the photograph they are thinner than a pixel of the view past a
        // few metres, and the fence faded to nothing.
        if (HdCutOut("textures/Fence003/color.jpg", "textures/Fence003/opacity.jpg", widen: 2) is not { } tex) return null;
        return new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Metallic = 0.6f,
            Roughness = 0.5f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Scale = new Vector3(size.X / tile, size.Y / tile, 1),
        };
    }

    /// <summary>How strongly the bare metal's streaks and blotches show (1: as in the photograph).</summary>
    const float GrimeContrast = 0.7f;

    /// <summary>The damaged parts' bare metal: the streaks and blotches of a rusty sheet (Metal041B) as shades of
    /// grey, most of the sheet a half, for the condition's colour, doubled, to go over.</summary>
    static Texture2D? Grime() => HdCached("grime", () =>
    {
        if (LoadHd("textures/Metal041B/color.jpg") is not { } img) return null;
        img.Convert(Image.Format.Rgb8);
        var d = img.GetData();
        int n = d.Length / 3;
        var lum = new float[n];
        var histogram = new int[1024];
        for (int i = 0; i < n; i++)
        {
            lum[i] = 0.2126f * toLinear[d[i * 3]] + 0.7152f * toLinear[d[i * 3 + 1]] + 0.0722f * toLinear[d[i * 3 + 2]];
            histogram[Math.Clamp((int)(lum[i] * 1023), 0, 1023)]++;
        }
        // The sheet's usual shade (its median) is the colour; the rust streaks go darker.
        int bin = 0;
        for (int seen = 0; bin < 1023 && (seen += histogram[bin]) < n / 2; bin++) { }
        float usual = Math.Max(1e-3f, (bin + 0.5f) / 1023);
        var shades = new byte[n];
        for (int i = 0; i < n; i++) shades[i] = ToSrgbByte(0.5f + 0.5f * GrimeContrast * (lum[i] / usual - 1));
        var grey = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.L8, shades);
        grey.GenerateMipmaps();
        return grey;
    });

    /// <summary>
    /// A damaged part's bare metal in our own look: the condition's colour (<paramref name="colour"/>, the one the
    /// original's flat colour was measured as) with a rusty sheet's streaks, bumps and roughness over it, so the
    /// condition still reads at a glance. Null without the pack.
    /// </summary>
    public static StandardMaterial3D? BareMetal(Color colour)
    {
        if (Grime() is not { } grime) return null;
        // Most of the grime is a half: the colour goes over it doubled (in linear terms), so most of the part is the colour.
        // A little darker than the flat colour: lit by the studio's key light the pinkish grey of a yellow part came out
        // near white.
        var lin = colour.SrgbToLinear() * 0.8f;
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = grime,
            AlbedoColor = new Color(ToSrgb(lin.R * 2), ToSrgb(lin.G * 2), ToSrgb(lin.B * 2)),
            Roughness = 1f,
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / 0.8f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (Hd("textures/Metal041B/normal.jpg", normal: true) is { } normal)
        {
            mat.NormalEnabled = true;
            mat.NormalTexture = normal;
            mat.NormalScale = 0.6f;
        }
        if (Hd("textures/Metal041B/roughness.jpg") is { } rough) mat.RoughnessTexture = rough;
        return mat;
    }

    // ---- the studio ------------------------------------------------------------------------------------------

    /// <summary>
    /// The WorkShop's studio: the original's plain grey behind the car; around it, for what the paint, the glass and the
    /// chrome reflect, a dark studio of our own (<see cref="StudioPanorama"/>), turned with the view as the lights are;
    /// light from all round, half of it from the studio; a filmic tone curve, ambient occlusion in the corners and a
    /// little glow on the lamps. <paramref name="transparent"/>: nothing behind (the parts' thumbnails).
    /// </summary>
    public static Godot.Environment Studio(Color background, bool transparent = false)
    {
        var sky = new Sky
        {
            RadianceSize = Sky.RadianceSizeEnum.Size512,
            SkyMaterial = new PanoramaSkyMaterial { Panorama = studio ??= ImageTexture.CreateFromImage(StudioPanorama()) },
        };
        return new Godot.Environment
        {
            BackgroundMode = transparent ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Color,
            BackgroundColor = background,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.62f, 0.62f, 0.64f),
            AmbientLightSkyContribution = 0.5f,
            AmbientLightEnergy = 0.7f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            SsaoEnabled = !transparent,
            SsaoRadius = 0.45f,
            SsaoIntensity = 1.6f,
            SsaoPower = 1.3f,
            GlowEnabled = true,
            GlowIntensity = 0.3f,
            GlowHdrThreshold = 1.4f,
        };
    }

    static ImageTexture? studio;

    /// <summary>
    /// The studio's surroundings, as the view sees them (the way it looks is straight ahead; it is turned with the
    /// camera): dark grey all round, the floor a little lighter towards the horizon; a wide softbox up beyond the car
    /// (a band of light along the bonnet and the roof), a long strip at the car's height behind the viewer (a line
    /// along the flanks) and the key light's own softbox over the viewer's left shoulder, where the key light comes from.
    /// </summary>
    static Image StudioPanorama()
    {
        const int w = 1024, h = 512;
        var px = new float[w * h * 3];
        // A soft-edged panel: its middle (azimuth, elevation), half its size, all in degrees, and its brightness.
        (float Az, float El, float HalfAz, float HalfEl, float Soft, float Light)[] panels =
        [
            (0, 42, 38, 13, 5, 1.5f),
            (180, 10, 70, 5, 3, 3.2f),
            (199, 40, 14, 11, 3, 4f),
        ];
        for (int y = 0; y < h; y++)
        {
            float el = 90 - (y + 0.5f) / h * 180;
            // Dark grey above, a lighter floor near the horizon, dark underneath.
            float grey = el >= 0 ? 0.045f + 0.02f * MathF.Exp(-el / 12) : 0.03f + 0.05f * MathF.Exp(el / 8);
            for (int x = 0; x < w; x++)
            {
                float az = (x + 0.5f) / w * 360;
                float v = grey;
                foreach (var p in panels)
                {
                    float da = MathF.Abs(((az - p.Az) % 360 + 540) % 360 - 180) * MathF.Cos(el * MathF.PI / 180);
                    float de = MathF.Abs(el - p.El);
                    float outside = MathF.Max(da - p.HalfAz, de - p.HalfEl);
                    v += p.Light * Math.Clamp(1 - outside / p.Soft, 0, 1) * Math.Clamp(1 - outside / p.Soft, 0, 1);
                }
                int i = (y * w + x) * 3;
                px[i] = v;
                px[i + 1] = v;
                px[i + 2] = v * 1.02f;
            }
        }
        var bytes = new byte[px.Length * 4];
        Buffer.BlockCopy(px, 0, bytes, 0, bytes.Length);
        return Image.CreateFromData(w, h, false, Image.Format.Rgbf, bytes);
    }

    /// <summary>The studio turned to face the way <paramref name="camera"/> looks (the panorama's straight ahead).</summary>
    public static void TurnStudio(Godot.Environment env, Camera3D camera) =>
        env.SkyRotation = new Vector3(0, camera.GlobalRotation.Y, 0);

    /// <summary>
    /// The studio's lights, turning with the view as the original's did: the key light from in front of the car, over the
    /// viewer's left shoulder (the original's: 40° up, 19° to the left), casting soft shadows; a weak fill from the right
    /// and below; and a rim light from behind that picks out the edges.
    /// </summary>
    public static void AddLights(Camera3D camera)
    {
        camera.AddChild(new DirectionalLight3D
        {
            Name = "Key",
            LightEnergy = 1.45f,
            // Its own glint small: the studio's softboxes give the paint its shine.
            LightSpecular = 0.18f,
            LightColor = new Color(1f, 0.98f, 0.95f),
            ShadowEnabled = true,
            ShadowBlur = 2f,
            DirectionalShadowMaxDistance = 14f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            // From behind the viewer, up and to the left, shining forward.
            Rotation = new Vector3(Mathf.DegToRad(-40f), Mathf.DegToRad(-19f), 0),
        });
        camera.AddChild(new DirectionalLight3D
        {
            Name = "Fill",
            LightEnergy = 0.3f,
            LightSpecular = 0.1f,
            LightColor = new Color(0.85f, 0.9f, 1f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            Rotation = new Vector3(Mathf.DegToRad(12f), Mathf.DegToRad(35f), 0),
        });
        camera.AddChild(new DirectionalLight3D
        {
            Name = "Rim",
            LightEnergy = 0.55f,
            LightSpecular = 0.3f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
            // From behind the car, above: facing back towards the viewer.
            Rotation = new Vector3(Mathf.DegToRad(-35f), Mathf.DegToRad(160f), 0),
        });
    }

    static Shader? contactShader;

    /// <summary>
    /// The soft darkening where something stands (the light from all round, kept off the floor by it), for a footprint
    /// of <paramref name="length"/> by <paramref name="width"/> metres: a rounded rectangle fading out over
    /// <paramref name="soft"/> past its edges. Lies flat at the node's origin; seen from above only.
    /// </summary>
    public static MeshInstance3D ContactShadow(float length, float width, float strength = 0.5f, float soft = 0.3f)
    {
        contactShader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
                uniform vec2 size;
                uniform vec2 half_box;
                uniform float soft;
                uniform float strength;
                void fragment() {
                    vec2 p = (UV - 0.5) * size;
                    vec2 q = abs(p) - half_box + vec2(soft);
                    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - soft;
                    ALBEDO = vec3(0.0);
                    ALPHA = strength * (1.0 - smoothstep(-soft, soft * 1.5, d));
                }
                """,
        };
        var half = new Vector2(length, width) * 0.5f;
        var size = half * 2 + new Vector2(soft * 5, soft * 5);
        var mat = new ShaderMaterial { Shader = contactShader, RenderPriority = -1 };
        mat.SetShaderParameter("size", size);
        mat.SetShaderParameter("half_box", half);
        mat.SetShaderParameter("soft", soft);
        mat.SetShaderParameter("strength", strength);
        var mi = new MeshInstance3D
        {
            Name = "ContactShadow",
            Mesh = new PlaneMesh { Size = size },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = mat,
        };
        mi.SetMeta("nopick", true);
        return mi;
    }

    /// <summary>A contact shadow's darkness, 0 to 1.</summary>
    public static void SetStrength(MeshInstance3D shadow, float strength) =>
        (shadow.MaterialOverride as ShaderMaterial)?.SetShaderParameter("strength", strength);

    // ---- outdoors ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The JunkYard's, the Car Lot's and the Auction's daylight: the sky behind, lighting everything from all round and
    /// reflected in the paint, the same filmic tone curve and ambient occlusion as the WorkShop's studio, a little haze.
    /// </summary>
    public static Godot.Environment Outdoor(Color horizon, float fogDensity)
    {
        var sky = new Sky
        {
            RadianceSize = Sky.RadianceSizeEnum.Size256,
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.3f, 0.5f, 0.82f),
                SkyHorizonColor = horizon,
                SkyCurve = 0.1f,
                // Below the horizon (past the end of the ground) the haze's colour.
                GroundBottomColor = horizon,
                GroundHorizonColor = horizon,
                SunAngleMax = 20f,
                SunCurve = 0.1f,
            },
        };
        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // Half of the light from all round from the sky, half a neutral grey: the shade is not blue.
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.8f, 0.79f, 0.76f),
            AmbientLightSkyContribution = 0.5f,
            AmbientLightEnergy = 0.65f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 0.95f,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.5f,
            SsaoPower = 1.3f,
            GlowEnabled = true,
            GlowIntensity = 0.25f,
            GlowHdrThreshold = 1.2f,
            FogEnabled = fogDensity > 0,
            FogLightColor = horizon,
            FogDensity = fogDensity,
            FogAerialPerspective = 0.6f,
            FogSkyAffect = 0.25f,
        };
    }

    /// <summary>The sun's shadows made soft (the sun is not a point), sharp where things touch the ground.</summary>
    public static void Soften(DirectionalLight3D sun)
    {
        sun.LightAngularDistance = 0.8f;
        sun.ShadowBlur = 1.2f;
        sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
    }

    /// <summary>A floor that shows only the shadows falling on it (the car keeps standing on the plain grey).</summary>
    public static MeshInstance3D ShadowCatcher(float y)
    {
        return new MeshInstance3D
        {
            Name = "ShadowCatcher",
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            Position = new Vector3(0, y, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadowToOpacity = true,
                AlbedoColor = new Color(0.12f, 0.12f, 0.13f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 1f,
            },
        };
    }
}
