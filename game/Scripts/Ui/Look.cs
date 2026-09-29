using System;
using Godot;

namespace OpenGG.Ui;

/// <summary>
/// The classic look, drawn in code (the original's own 2D art sits in encrypted archives and is not used):
/// gritty dark metal, gold command plates, grey tab plates, black Parts Bin, yellow dialogs.
/// Colours were sampled from screen captures of the original at 640 × 480.
/// </summary>
public static class Look
{
    public static readonly Color Metal = C("#313431");
    public static readonly Color MetalDark = C("#292829");
    public static readonly Color MetalLight = C("#424542");
    public static readonly Color ViewGrey = C("#4a494a");
    public static readonly Color Gold = C("#b58a31");
    public static readonly Color GoldLight = C("#d6aa4a");
    public static readonly Color GoldDark = C("#6b5018");
    public static readonly Color GoldText = C("#2e2612");
    public static readonly Color PlateGrey = C("#9c9a9c");
    public static readonly Color PlateText = C("#4a4d4a");
    public static readonly Color TabActive = C("#181818");
    public static readonly Color TabActiveText = C("#cfcfcf");
    public static readonly Color TitleGrey = C("#5a5d5a");
    public static readonly Color TitleLight = C("#7b7d7b");
    public static readonly Color MoneyGreen = C("#4aa66b");
    public static readonly Color MoneyBox = C("#292829");
    public static readonly Color Bin = C("#000000");
    public static readonly Color DialogYellow = C("#d8a431");
    public static readonly Color DialogText = C("#1a1406");
    public static readonly Color DialogTitle = C("#101010");
    public static readonly Color DialogTitleText = C("#e0b449");
    public static readonly Color Orange = C("#e89a2a");
    public static readonly Color White = C("#f0f0f0");
    public static readonly Color Muted = C("#a6a6a6");

    static Color C(string hex) => Color.FromHtml(hex);

    public static Font Condensed { get; private set; } = null!;
    public static Font CondensedBold { get; private set; } = null!;
    public static Font Heavy { get; private set; } = null!;
    public static Font Impact { get; private set; } = null!;
    public static Font Plain { get; private set; } = null!;

    static bool fontsReady;

    /// <summary>The fonts in game/Fonts (OFL, see its LICENSES.md), the same on every system; each stands in for the
    /// Windows font named (their widths within a few per cent of it, so the lettering fits as it did).</summary>
    public static void LoadFonts()
    {
        if (fontsReady) return;
        fontsReady = true;
        Condensed = Font("OpenGGNarrow-SemiBold");  // Bahnschrift, condensed
        CondensedBold = Font("OpenGGNarrow-Bold");
        Heavy = Font("ArchivoBlack-Regular");  // Arial Black
        Impact = Font("Anton-Regular");  // Impact
        var ts = TextServerManager.Singleton.GetPrimaryInterface();
        Plain = new FontVariation  // Tahoma bold
        {
            BaseFont = Font("OpenSans-Variable"),
            VariationOpentype = new Godot.Collections.Dictionary { [ts.NameToTag("wght")] = 700, [ts.NameToTag("wdth")] = 100 },
        };
    }

    static FontFile Font(string file)
    {
        var f = GD.Load<FontFile>($"res://Fonts/{file}.ttf");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.Hinting = TextServer.Hinting.Light;
        return f;
    }

    // ---- textures --------------------------------------------------------------------------------------

    static ImageTexture? metal, gold, yellow, grey;

    /// <summary>Speckled, slightly smudged metal: a tile of random greys around the base colour.</summary>
    static ImageTexture Grit(Color baseColor, float spread, int seed, int smudges)
    {
        const int n = 128;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
        var rnd = new Random(seed);
        var buf = new float[n * n];
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(rnd.NextDouble() * 2 - 1);
        // Soften the speckle a little so it reads as worn metal, not TV noise.
        var soft = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float s = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        s += buf[((y + dy + n) % n) * n + (x + dx + n) % n] * (dx == 0 && dy == 0 ? 4 : 1);
                soft[y * n + x] = s / 12f;
            }
        for (int k = 0; k < smudges; k++)
        {
            int cx = rnd.Next(n), cy = rnd.Next(n), r = 6 + rnd.Next(18);
            float dark = (float)(rnd.NextDouble() * 0.9 + 0.3);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y) / r;
                    if (d > 1) continue;
                    soft[((cy + y + n) % n) * n + (cx + x + n) % n] -= (1 - d) * (1 - d) * dark;
                }
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = soft[y * n + x] * spread;
                img.SetPixel(x, y, new Color(
                    Mathf.Clamp(baseColor.R + v, 0, 1),
                    Mathf.Clamp(baseColor.G + v, 0, 1),
                    Mathf.Clamp(baseColor.B + v * 0.9f, 0, 1)));
            }
        return ImageTexture.CreateFromImage(img);
    }

    public static Texture2D MetalTexture => metal ??= Grit(Metal, 0.07f, 11, 10);
    public static Texture2D GoldTexture => gold ??= Grit(Gold, 0.09f, 23, 6);
    public static Texture2D YellowTexture => yellow ??= Grit(DialogYellow, 0.08f, 37, 8);
    public static Texture2D GreyTexture => grey ??= Grit(C("#424142"), 0.06f, 41, 4);

    /// <summary>A tiled texture panel with a thin bevel drawn over it.</summary>
    public static StyleBoxTexture Tiled(Texture2D tex, int margin = 0)
    {
        var sb = new StyleBoxTexture
        {
            Texture = tex,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
        };
        sb.ContentMarginLeft = sb.ContentMarginRight = sb.ContentMarginTop = sb.ContentMarginBottom = margin;
        return sb;
    }

    public static StyleBoxFlat Flat(Color bg, Color? border = null, int width = 0, int radius = 0, int pad = 0)
    {
        var sb = new StyleBoxFlat { BgColor = bg, AntiAliasing = false };
        if (border is { } b)
        {
            sb.BorderColor = b;
            sb.SetBorderWidthAll(width);
        }
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = sb.ContentMarginRight = sb.ContentMarginTop = sb.ContentMarginBottom = pad;
        return sb;
    }

    // ---- theme ------------------------------------------------------------------------------------------

    public static Theme Build()
    {
        LoadFonts();
        var t = new Theme { DefaultFont = CondensedBold, DefaultFontSize = 11 };
        t.SetColor("font_color", "Label", White);
        t.SetStylebox("panel", "TooltipPanel", Flat(C("#000000e6"), C("#6b696b"), 1, 0, 3));
        t.SetColor("font_color", "TooltipLabel", White);
        t.SetFontSize("font_size", "TooltipLabel", 10);
        t.SetStylebox("normal", "LineEdit", Flat(C("#ffffff00"), null, 0, 0, 2));
        t.SetStylebox("focus", "LineEdit", new StyleBoxEmpty());
        t.SetColor("font_color", "LineEdit", C("#3a4ea0"));
        t.SetColor("caret_color", "LineEdit", C("#3a4ea0"));
        t.SetFont("font", "LineEdit", Heavy);
        t.SetFontSize("font_size", "LineEdit", 14);
        t.SetColor("default_color", "RichTextLabel", White);
        t.SetFont("normal_font", "RichTextLabel", CondensedBold);
        t.SetFont("bold_font", "RichTextLabel", CondensedBold);
        t.SetFontSize("normal_font_size", "RichTextLabel", 12);
        t.SetFontSize("bold_font_size", "RichTextLabel", 12);
        return t;
    }
}

/// <summary>A plain rectangle of the classic metal, tiled.</summary>
public partial class MetalRect : Control
{
    public Texture2D? Tex { get; set; }
    public Color Tint { get; set; } = Colors.White;

    public MetalRect() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw() => DrawTextureRect(Tex ?? Look.MetalTexture, new Rect2(Vector2.Zero, Size), tile: true, modulate: Tint);
}

/// <summary>A bevelled edge: light on top-left, dark on bottom-right (or the reverse for sunk).</summary>
public static class Bevel
{
    public static void Draw(CanvasItem c, Rect2 r, Color light, Color dark, int w = 1)
    {
        for (int i = 0; i < w; i++)
        {
            c.DrawLine(new Vector2(r.Position.X + i, r.Position.Y + i), new Vector2(r.End.X - 1 - i, r.Position.Y + i), light);
            c.DrawLine(new Vector2(r.Position.X + i, r.Position.Y + i), new Vector2(r.Position.X + i, r.End.Y - 1 - i), light);
            c.DrawLine(new Vector2(r.Position.X + i, r.End.Y - 1 - i), new Vector2(r.End.X - 1 - i, r.End.Y - 1 - i), dark);
            c.DrawLine(new Vector2(r.End.X - 1 - i, r.Position.Y + i), new Vector2(r.End.X - 1 - i, r.End.Y - 1 - i), dark);
        }
    }
}

/// <summary>Text with a light edge above-left and a dark one below-right, like the original's pressed-in lettering.</summary>
public partial class EmbossLabel : Control
{
    public string Text { get; set; } = "";
    public Font? Font { get; set; }
    public int FontSize { get; set; } = 12;
    public Color Face { get; set; } = Look.TitleGrey;
    public Color Light { get; set; } = Look.TitleLight;
    public Color Dark { get; set; } = new(0, 0, 0, 0.8f);
    public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Center;
    /// <summary>Draw a dark outline around the letters (tab plates).</summary>
    public int Outline { get; set; }
    public Color OutlineColor { get; set; } = new(0, 0, 0, 0.9f);

    public EmbossLabel() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var f = Font ?? Look.CondensedBold;
        float asc = f.GetAscent(FontSize), desc = f.GetDescent(FontSize);
        float y = (Size.Y - (asc + desc)) / 2 + asc;
        var pos = new Vector2(0, y);
        if (Outline > 0) DrawStringOutline(f, pos, Text, Align, Size.X, FontSize, Outline, OutlineColor);
        DrawString(f, pos + new Vector2(-1, -1), Text, Align, Size.X, FontSize, Light);
        DrawString(f, pos + new Vector2(1, 1), Text, Align, Size.X, FontSize, Dark);
        DrawString(f, pos, Text, Align, Size.X, FontSize, Face);
    }
}
