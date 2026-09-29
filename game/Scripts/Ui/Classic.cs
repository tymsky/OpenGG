using System;
using System.Linq;
using Godot;

namespace OpenGG.Ui;

/// <summary>A button that draws itself (the classic plates, tabs and tool icons).</summary>
public abstract partial class DrawnButton : BaseButton
{
    protected bool Hovered { get; private set; }

    /// <summary>Called for every press of any button (a skin's click sound).</summary>
    public static Action? AnyPressed { get; set; }

    /// <summary>No click when pressed: the view tabs, the tools, the Catalog's tabs and pages and the like, which make
    /// sounds of their own in the original, or none (measured).</summary>
    public bool Quiet { get; set; }

    protected DrawnButton()
    {
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ButtonDown += () =>
        {
            if (!Quiet) AnyPressed?.Invoke();
        };
        MouseEntered += () => { Hovered = true; QueueRedraw(); };
        MouseExited += () => { Hovered = false; QueueRedraw(); };
        ButtonDown += QueueRedraw;
        ButtonUp += QueueRedraw;
    }

    protected bool Down => GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;

    /// <summary>With a skin: the screen whose up and down pictures this button is cut out of.</summary>
    public string? SkinScreen { get; set; }
    /// <summary>With a skin: this button's own up and down pictures instead.</summary>
    public Texture2D? SkinUp { get; set; }
    public Texture2D? SkinDown { get; set; }

    /// <summary>
    /// Draws the skin's picture of this button: up, down, or (null) nothing, where the empty screen
    /// already shows the place. False when the skin has no picture for it.
    /// </summary>
    protected bool DrawSkin(bool? down)
    {
        if (SkinUp is not null || SkinDown is not null)
        {
            if (down is { } d && (d ? SkinDown ?? SkinUp : SkinUp) is { } t) DrawTextureRect(t, new Rect2(Vector2.Zero, Size), false);
            return true;
        }
        if (SkinScreen is null || UiSkin.Screen(SkinScreen) is not { } s) return false;
        // Grown a little: a pressed plate reaches a pixel or three past the raised one.
        if (down is { } dn && (dn ? s.Down : s.Up) is { } tex) UiSkin.DrawUnder(this, tex, new Rect2(Vector2.Zero, Size), 3);
        return true;
    }

    protected void Text(string text, Font font, int size, Vector2 pos, float width, HorizontalAlignment align, Color color, int outline = 0, Color? outlineColor = null)
    {
        if (outline > 0) DrawStringOutline(font, pos, text, align, width, size, outline, outlineColor ?? Colors.Black);
        DrawString(font, pos, text, align, width, size, color);
    }

    protected float Baseline(Font f, int size, float height) => (height - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size);
}

/// <summary>
/// A command in the right-hand column: a gold plate with dark lettering and a small picture on the
/// right (the original's "Show Condition", "Get A Job", "Go To JunkYard ▸" ...).
/// </summary>
public partial class GoldPlate : DrawnButton
{
    public string Label { get; set; } = "";
    public Texture2D? Icon { get; set; }
    /// <summary>Draws the ▸ that the "Go To ..." commands have.</summary>
    public bool Arrow { get; set; }
    public int FontSize { get; set; } = 17;
    /// <summary>Colour chips (Show Condition).</summary>
    public bool Chips { get; set; }
    public bool Lit { get; set; }

    public override void _Draw()
    {
        if (DrawSkin(Down || Lit)) return;
        var r = new Rect2(Vector2.Zero, Size);
        bool down = Down || Lit;
        DrawTextureRect(Look.GoldTexture, r, tile: true, modulate: down ? new Color(0.78f, 0.78f, 0.78f) : Hovered ? new Color(1.12f, 1.1f, 1.05f) : Colors.White);
        DrawRect(r, new Color(0, 0, 0, 0.85f), filled: false, width: 1);
        var inner = r.Grow(-1);
        if (down) Bevel.Draw(this, inner, Look.GoldDark, Look.GoldLight);
        else Bevel.Draw(this, inner, Look.GoldLight, Look.GoldDark);
        var off = down ? new Vector2(1, 1) : Vector2.Zero;
        float right = Size.X - 4;
        if (Arrow)
        {
            float cy = Size.Y / 2;
            DrawColoredPolygon([new Vector2(right - 5, cy - 5) + off, new Vector2(right, cy) + off, new Vector2(right - 5, cy + 5) + off], Look.GoldText);
            right -= 9;
        }
        if (Chips)
        {
            Color[] cs = [Color.FromHtml("#101010"), Color.FromHtml("#7a1a10"), Color.FromHtml("#d8352a"), Color.FromHtml("#e87a20"), Color.FromHtml("#e8c030"), Color.FromHtml("#a8c83a"), Color.FromHtml("#46c25a")];
            float x = Size.X - 4 - cs.Length * 4;
            for (int i = 0; i < cs.Length; i++) DrawRect(new Rect2(new Vector2(x + i * 4, Size.Y - 8) + off, new Vector2(3, 4)), cs[i]);
        }
        else if (Icon is not null)
        {
            var isz = new Vector2(18, 14);
            DrawTextureRect(Icon, new Rect2(new Vector2(right - isz.X, Size.Y - isz.Y - 2) + off, isz), false);
            right -= isz.X + 2;
        }
        var f = Look.CondensedBold;
        var lines = Label.Split('\n');
        float lh = FontSize + 1;
        float top = Baseline(f, FontSize, Size.Y) - (lines.Length - 1) * lh / 2;
        var align = lines.Length > 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        for (int i = 0; i < lines.Length; i++)
            Text(lines[i], f, FontSize, new Vector2(4, top + i * lh) + off, right - 4, align, Look.GoldText);
    }
}

/// <summary>An empty, sunk slot where a command is not available (the original keeps the column's layout).</summary>
public partial class BlankPlate : Control
{
    /// <summary>The skin's empty screen shows the slot already.</summary>
    public bool Skinned { get; set; }

    public BlankPlate() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (Skinned) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.MetalTexture, r, tile: true, modulate: new Color(0.82f, 0.82f, 0.82f));
        Bevel.Draw(this, r, new Color(0, 0, 0, 0.75f), new Color(1, 1, 1, 0.16f));
    }
}

/// <summary>
/// The band the view tabs sit on, from the screen's left edge to the money box (measured on the original's WorkShop:
/// y 41 to 80, a grey a little darker than the tabs).
/// </summary>
public partial class TabStrip : Control
{
    public TabStrip() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (UiSkin.Active) return;  // the skin's screen has its own
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GreyTexture, r, tile: true, modulate: new Color(1.25f, 1.25f, 1.25f));
        DrawRect(r, Color.FromHtml("#242424"), filled: false, width: 1);
        Bevel.Draw(this, r.Grow(-1), new Color(1, 1, 1, 0.12f), new Color(0, 0, 0, 0.35f));
    }
}

/// <summary>
/// A view tab: a grey plate. The other views' names are stamped into it (grey letters with a light and a dark edge);
/// the chosen view's plate is darker and its name printed in black, in the same letters. A view the job doesn't allow
/// is an empty plate.
/// </summary>
public partial class TabPlate : DrawnButton
{
    public string Label { get; set; } = "";
    public bool Active { get; set; }
    public bool Blank { get; set; }

    public override void _Draw()
    {
        if (DrawSkin(Blank ? null : Active)) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GreyTexture, r, tile: true, modulate: Active ? new Color(1.17f, 1.17f, 1.17f) : new Color(1.6f, 1.6f, 1.6f));
        DrawRect(r, Color.FromHtml("#242424"), filled: false, width: 1);
        Bevel.Draw(this, r.Grow(-1), new Color(1, 1, 1, 0.45f), new Color(0, 0, 0, 0.45f));
        if (Blank) return;
        // The same letters on every tab, as in the original: the chosen one printed black, the others stamped.
        var g = Look.Impact;
        const int gsize = 24;
        var gp = new Vector2(0, Baseline(g, gsize, Size.Y) + 1);
        string text = Spaced(Label);
        if (Active)
        {
            DrawString(g, gp, text, HorizontalAlignment.Center, Size.X, gsize, Color.FromHtml("#0c0c0c"));
            return;
        }
        DrawString(g, gp + new Vector2(1, 1), text, HorizontalAlignment.Center, Size.X, gsize, Color.FromHtml("#1a1a1a"));
        DrawString(g, gp + new Vector2(-1, -1), text, HorizontalAlignment.Center, Size.X, gsize, Color.FromHtml("#d0d0d0"));
        DrawString(g, gp, text, HorizontalAlignment.Center, Size.X, gsize, Hovered ? Color.FromHtml("#8a8a8a") : Color.FromHtml("#707070"));
    }

    /// <summary>The original spaces the tab lettering out a little.</summary>
    static string Spaced(string s) => string.Join('\u200A', s.ToCharArray());
}

/// <summary>The money box under the screen title: green figures in a sunk dark box.</summary>
public partial class MoneyBox : Control
{
    string text = "";
    Color color = Look.MoneyGreen;

    public MoneyBox() => MouseFilter = MouseFilterEnum.Pass;

    public void Set(string t, Color c)
    {
        if (t == text && c == color) return;
        text = t;
        color = c;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (UiSkin.Active && UiSkin.Font("cash") is { } cash)
        {
            // The original's green figures, right-aligned in the box the screen already has.
            var at = L.Orig.Money - GetGlobalTransform().Origin;
            float w = cash.GetStringSize(text, HorizontalAlignment.Left, -1, UiSkin.FontSize(cash)).X;
            DrawString(cash, new Vector2(at.X - w, at.Y + cash.GetAscent(UiSkin.FontSize(cash))), text, HorizontalAlignment.Left, -1, UiSkin.FontSize(cash), color == Look.MoneyGreen ? Colors.White : color);
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Look.MoneyBox);
        Bevel.Draw(this, r, new Color(0, 0, 0, 0.9f), new Color(1, 1, 1, 0.12f), 2);
        var f = Look.CondensedBold;
        const int size = 19;
        var pos = new Vector2(0, (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size));
        DrawStringOutline(f, pos, text, HorizontalAlignment.Right, Size.X - 6, size, 3, new Color(0, 0, 0, 0.9f));
        DrawString(f, pos, text, HorizontalAlignment.Right, Size.X - 6, size, color);
    }
}

/// <summary>A tool: a round picture with its name bent along an arc underneath, as in the original.</summary>
public partial class ToolIcon : DrawnButton
{
    public string Label { get; set; } = "";
    public Texture2D? Icon { get; set; }
    public bool Selected { get; set; }
    /// <summary>Radius of the picture's disc.</summary>
    public float Radius { get; set; } = 16;

    public override void _Draw()
    {
        if (DrawSkin(Selected || Down)) return;
        var c = new Vector2(Size.X / 2, Radius + 2);
        if (Selected)
        {
            DrawCircle(c, Radius + 1, Color.FromHtml("#d6aa4a"));
            DrawCircle(c, Radius - 1, Color.FromHtml("#141414"));
        }
        var tint = Selected ? Color.FromHtml("#e0b452") : Hovered ? Color.FromHtml("#d8d8d8") : Color.FromHtml("#a8a8a8");
        if (Icon is not null)
        {
            float s = Radius * 1.45f;
            DrawTextureRect(Icon, new Rect2(c - new Vector2(s, s) / 2, new Vector2(s, s)), false, tint);
        }
        ArcText(c, Radius + 15, Label, Look.CondensedBold, 17, Color.FromHtml("#d8a53a"));
    }

    /// <summary>Letters along the lower arc of a circle, reading left to right, tops towards the centre.</summary>
    void ArcText(Vector2 centre, float radius, string text, Font f, int size, Color color)
    {
        float total = f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        float angle = Mathf.Pi / 2 + total / radius / 2;
        foreach (char ch in text)
        {
            float w = f.GetCharSize(ch, size).X;
            float mid = angle - w / radius / 2;
            var p = centre + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * radius;
            float rot = mid - Mathf.Pi / 2;
            DrawSetTransform(p, rot, Vector2.One);
            var s = ch.ToString();
            var o = new Vector2(-w / 2, f.GetAscent(size) * 0.35f);
            DrawStringOutline(f, o, s, HorizontalAlignment.Left, -1, size, 3, new Color(0, 0, 0, 0.85f));
            DrawString(f, o, s, HorizontalAlignment.Left, -1, size, color);
            angle -= w / radius;
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>REPAIR and SCRAP: grey plates with big pressed-in lettering and a picture, where a part carried from the
/// Parts Bin is let go. The original's stay as they are (measured: under the pointer and under a carried part alike);
/// ours light up.</summary>
public partial class DropPlate : Control
{
    public string Label { get; set; } = "";
    public Texture2D? Icon { get; set; }
    /// <summary>The skin's empty screen shows the plate already.</summary>
    public bool Skinned { get; set; }
    bool over, carriedOver;

    public DropPlate()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { over = true; QueueRedraw(); };
        MouseExited += () => { over = false; QueueRedraw(); };
    }

    /// <summary>A part being carried is over the plate (the pointer stays with the slot it was picked up from, so the
    /// plate itself hears nothing of it).</summary>
    public bool CarriedOver
    {
        get => carriedOver;
        set
        {
            if (carriedOver == value) return;
            carriedOver = value;
            QueueRedraw();
        }
    }

    bool Lit => over || carriedOver;

    public override void _Draw()
    {
        if (Skinned) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GreyTexture, r, tile: true, modulate: Lit ? new Color(1.35f, 1.3f, 1.2f) : Colors.White);
        Bevel.Draw(this, r, new Color(1, 1, 1, 0.22f), new Color(0, 0, 0, 0.8f), 2);
        if (Icon is not null) DrawTextureRect(Icon, new Rect2(new Vector2(Size.X - 40, 6), new Vector2(34, Size.Y - 12)), false, new Color(0.95f, 0.75f, 0.3f));
        var f = Look.Heavy;
        const int size = 17;
        var pos = new Vector2(4, (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size));
        DrawString(f, pos + new Vector2(-1, -1), Label, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.25f));
        DrawStringOutline(f, pos, Label, HorizontalAlignment.Left, -1, size, 2, new Color(0, 0, 0, 0.8f));
        DrawString(f, pos, Label, HorizontalAlignment.Left, -1, size, Color.FromHtml("#5a5d5a"));
    }
}

/// <summary>The Parts Bin's ▲ and ▼: small gold buttons with a black triangle.</summary>
public partial class ArrowButton : DrawnButton
{
    public bool Up { get; set; }
    /// <summary>Left or right instead of up or down.</summary>
    public bool Horizontal { get; set; }

    public override void _Draw()
    {
        // With a skin the empty screen has the arrows; only a pressed one is drawn.
        if (DrawSkin(Down && !Disabled ? true : null)) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawTextureRect(Look.GoldTexture, r, tile: true, modulate: Disabled ? new Color(0.5f, 0.5f, 0.5f) : Down ? new Color(0.8f, 0.8f, 0.8f) : Colors.White);
        DrawRect(r, Colors.Black, filled: false, width: 1);
        Bevel.Draw(this, r.Grow(-1), Down ? Look.GoldDark : Look.GoldLight, Down ? Look.GoldLight : Look.GoldDark);
        var c = Size / 2 + (Down ? Vector2.One : Vector2.Zero);
        float a = Mathf.Min(Size.X, Size.Y) * 0.32f;
        Vector2[] tri = Horizontal
            ? Up ? [c + new Vector2(-a, 0), c + new Vector2(a * 0.8f, -a), c + new Vector2(a * 0.8f, a)] : [c + new Vector2(a, 0), c + new Vector2(-a * 0.8f, -a), c + new Vector2(-a * 0.8f, a)]
            : Up ? [c + new Vector2(0, -a), c + new Vector2(-a, a * 0.8f), c + new Vector2(a, a * 0.8f)] : [c + new Vector2(0, a), c + new Vector2(-a, -a * 0.8f), c + new Vector2(a, -a * 0.8f)];
        DrawColoredPolygon(tri, Colors.Black);
    }
}

/// <summary>The dark title tab above the Parts Bin and the Purchase Bin.</summary>
public partial class BinTab : Control
{
    public string Label { get; set; } = "Parts Bin";
    public bool Skinned { get; set; }

    public BinTab() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (Skinned) return;
        var w = Size.X;
        DrawColoredPolygon([new Vector2(0, Size.Y), new Vector2(0, 0), new Vector2(w - 12, 0), new Vector2(w, Size.Y)], Color.FromHtml("#000000"));
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(6, Size.Y - 3), Label, HorizontalAlignment.Left, -1, 11, Look.White);
    }
}

/// <summary>The yellow "ASSEMBLED" tag, in the colour of the worst part.</summary>
public partial class AssembledTag : Control
{
    /// <summary>
    /// An overlay picture of the 3D view as the original draws it: half a pixel to the right, each pixel shared between
    /// two of the picture's columns, so its strokes come out a touch soft (fitted on captures: the ASSEMBLED stamp 7.3
    /// off in the colour numbers against 10.2 drawn crisp, the arrow-keys hint 3.6 against 8.1; no shift down fitted).
    /// </summary>
    public static void DrawOverlay(CanvasItem item, Texture2D pic)
    {
        item.TextureFilter = TextureFilterEnum.Linear;
        item.DrawTexture(pic, new Vector2(0.5f, 0));
    }

    public Color Tint { get; set; } = Colors.Green;
    /// <summary>The worst part's condition (for the skin's red, yellow and green tags).</summary>
    public int Condition { get; set; }

    public AssembledTag() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (UiSkin.Active && UiSkin.Picture(Condition >= Core.Content.Condition.Green ? "assembled.green" : Condition == Core.Content.Condition.Yellow ? "assembled.yellow" : "assembled.red") is { } pic)
        {
            Material = UiSkin.Additive;
            DrawOverlay(this, pic);
            return;
        }
        Material = null;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r.Grow(-1), Tint, filled: false, width: 2);
        var f = Look.Heavy;
        const int size = 12;
        DrawString(f, new Vector2(0, (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size)), "ASSEMBLED", HorizontalAlignment.Center, Size.X, size, Tint);
    }
}

/// <summary>The arrow-keys picture in the corner of the 3D view ("Use arrow keys to rotate view").</summary>
public partial class ArrowKeysHint : Control
{
    public ArrowKeysHint() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (UiSkin.Active && UiSkin.Picture("usekeys") is { } pic)
        {
            Material = UiSkin.Additive;
            AssembledTag.DrawOverlay(this, pic);
            return;
        }
        Material = null;
        var key = new Vector2(7, 6);
        void Key(Vector2 p)
        {
            var r = new Rect2(p, key);
            DrawRect(r, Color.FromHtml("#d6cfd6"));
            DrawRect(r, Colors.Black, filled: false, width: 1);
        }
        Key(new Vector2(9, 0));
        Key(new Vector2(1, 7));
        Key(new Vector2(9, 7));
        Key(new Vector2(17, 7));
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(28, 13), "Use arrow keys to rotate view", HorizontalAlignment.Left, -1, 13, Color.FromHtml("#d6d6d6"));
    }
}

/// <summary>Bolt mode's "BOLT" labels: one per bolt still to do, with a leader line, in a cluster up and to the right of the part.</summary>
public partial class BoltLabels : Control
{
    public Vector2[] Points { get; set; } = [];

    public BoltLabels() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (Points.Length == 0) return;
        if (UiSkin.Active && UiSkin.Picture("bolt") is { } pic)
        {
            // The original's label: a picture added onto the view, its leader line ending on the bolt.
            Material = UiSkin.Additive;
            var picSize = pic.GetSize() * L.Orig.BoltScale;
            foreach (var p in Points)
                DrawTextureRect(pic, new Rect2(p - L.Orig.BoltLineEnd * L.Orig.BoltScale, picSize), false);
            return;
        }
        Material = null;
        float maxX = float.MinValue, minY = float.MaxValue;
        foreach (var p in Points)
        {
            maxX = Mathf.Max(maxX, p.X);
            minY = Mathf.Min(minY, p.Y);
        }
        var f = Look.CondensedBold;
        const int size = 10;
        var ink = Color.FromHtml("#c8c8c8");
        var order = Points.Select((p, i) => (p, i)).OrderBy(t => t.p.Y).ToArray();
        float x0 = Mathf.Min(maxX + 22, Size.X - 34);
        float y0 = Mathf.Max(14, minY - 16);
        for (int k = 0; k < order.Length; k++)
        {
            var from = order[k].p;
            var label = new Vector2(x0 + k * 3, y0 + k * 11);
            DrawLine(from, label + new Vector2(0, 2), ink, 1);
            DrawLine(label + new Vector2(0, 2), label + new Vector2(24, 2), ink, 1);
            DrawString(f, label, "BOLT", HorizontalAlignment.Left, -1, size, ink);
        }
    }
}
