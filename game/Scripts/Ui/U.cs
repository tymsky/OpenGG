using System;
using System.Globalization;
using System.Text;
using Godot;
using OpenGG.Core.Content;

namespace OpenGG.Ui;

/// <summary>Small builders for UI made in code.</summary>
public static class U
{
    public static T With<T>(this T node, Action<T> init) where T : Node
    {
        init(node);
        return node;
    }

    public static T Add<T>(this Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    public static Label Label(string text, int size = 14, Color? color = null, Font? font = null, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (font is not null) l.AddThemeFontOverride("font", font);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static Button Btn(string text, Action onPressed, string variation = "OButton", Texture2D? icon = null, string? tip = null, bool disabled = false)
    {
        var b = new Button { Text = text, ThemeTypeVariation = variation, Icon = icon, Disabled = disabled, FocusMode = Control.FocusModeEnum.None };
        if (tip is not null) b.TooltipText = tip;
        b.Pressed += onPressed;
        return b;
    }

    public static HBoxContainer HBox(int sep = 6, params Control?[] kids)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids)
            if (k is not null) box.AddChild(k);
        return box;
    }

    public static VBoxContainer VBox(int sep = 6, params Control?[] kids)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids)
            if (k is not null) box.AddChild(k);
        return box;
    }

    public static PanelContainer Panel(string variation, Control? child = null)
    {
        var p = new PanelContainer { ThemeTypeVariation = variation };
        if (child is not null) p.AddChild(child);
        return p;
    }

    public static MarginContainer Margin(Control child, int left, int top, int right, int bottom)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        m.AddChild(child);
        return m;
    }

    public static Control Space(float w = 0, float h = 0) => new() { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Control Expand() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static T ExpandH<T>(this T c) where T : Control
    {
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static T ExpandV<T>(this T c) where T : Control
    {
        c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static T MinSize<T>(this T c, float w, float h) where T : Control
    {
        c.CustomMinimumSize = new Vector2(w, h);
        return c;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            n.RemoveChild(c);
            c.QueueFree();
        }
    }

    public static RichTextLabel Rich(string bbcode, int size = 14, Color? color = null)
    {
        var r = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = bbcode,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        r.AddThemeFontSizeOverride("normal_font_size", size);
        r.AddThemeFontSizeOverride("bold_font_size", size);
        if (color is { } c) r.AddThemeColorOverride("default_color", c);
        return r;
    }

    public static string Escape(string s) => s.Replace("[", "[lb]");

    /// <summary>Request text: words between *asterisks* are highlighted.</summary>
    public static string Highlight(string text, string color = "#0a6a1a")
    {
        var sb = new StringBuilder();
        var parts = Escape(text).Split('*');
        for (int i = 0; i < parts.Length; i++)
            sb.Append(i % 2 == 1 ? $"[color={color}][b]{parts[i]}[/b][/color]" : parts[i]);
        return sb.ToString();
    }

    public static string Money(decimal v) => (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Hours and minutes, as the original shows play and repair time.</summary>
    public static string Hm(double seconds)
    {
        int m = (int)(seconds / 60);
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>Hours, minutes and seconds (TOTAL TIME, the Car Lot's Repair Time).</summary>
    public static string Hms(double seconds)
    {
        int s = (int)seconds;
        return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
    }

    public static Color CondColor(int condition) => Color.FromHtml(Condition.Colors[Math.Clamp(condition, 0, 3)]);

    public static TextureRect Pic(Texture2D? tex, float w, float h) => new()
    {
        Texture = tex,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(w, h),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static Control Swatch(Color c, float w, float h) => new ColorRect { Color = c, CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
}

/// <summary>The condition triangle in the top-left corner of a Parts Bin slot.</summary>
public partial class CondTriangle : Control
{
    public int Condition { get; set; }

    public CondTriangle()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(22, 22);
        Size = CustomMinimumSize;
    }

    public override void _Draw()
    {
        var s = Size;
        DrawColoredPolygon([new Vector2(0, 0), new Vector2(s.X, 0), new Vector2(0, s.Y)], new Godot.Color(0.75f, 0.75f, 0.75f));
        DrawColoredPolygon([new Vector2(0, 0), new Vector2(s.X - 2, 0), new Vector2(0, s.Y - 2)], U.CondColor(Condition));
    }
}

/// <summary>The Auction's clock: a red wedge on a white face, like a pie chart.</summary>
public partial class PieClock : Control
{
    /// <summary>With a skin: its strip of clock faces (square, left to right as time runs out).</summary>
    public Texture2D? Faces { get; set; }

    public float Fraction { get; set; }

    public PieClock() => CustomMinimumSize = new Vector2(46, 46);

    public override void _Draw()
    {
        if (Faces is not null)
        {
            // The first face is all red (all the time left), the last almost white.
            int n = Math.Max(1, Faces.GetWidth() / Faces.GetHeight());
            int k = Math.Clamp((int)((1 - Fraction) * n), 0, n - 1);
            int side = Faces.GetHeight();
            DrawTextureRectRegion(Faces, new Rect2(Vector2.Zero, new Vector2(side, side)), new Rect2(k * side, 0, side, side));
            return;
        }
        var c = Size / 2;
        float r = Mathf.Min(c.X, c.Y) - 2;
        DrawCircle(c, r + 2, Colors.Black);
        DrawCircle(c, r, Colors.White);
        float f = Mathf.Clamp(Fraction, 0, 1);
        if (f > 0.001f)
        {
            int n = Math.Max(3, (int)(48 * f));
            var pts = new Vector2[n + 2];
            pts[0] = c;
            for (int i = 0; i <= n; i++)
            {
                float a = -Mathf.Pi / 2 + Mathf.Tau * f * i / n;
                pts[i + 1] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            DrawColoredPolygon(pts, new Godot.Color(0.85f, 0.15f, 0.12f));
        }
    }
}
