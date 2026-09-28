using System;
using System.Collections.Generic;
using Godot;

namespace OpenGG.Ui;

/// <summary>The Catalog: an open ring binder on the metal background.</summary>
public partial class Binder : Control
{
    /// <summary>The decal pages are black.</summary>
    public bool Dark { get; set; }

    public Binder() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var cover = new Rect2(Vector2.Zero, Size);
        DrawRect(cover.Grow(2), new Color(0, 0, 0, 0.6f));
        DrawTextureRect(Look.GreyTexture, cover, tile: true, modulate: new Color(1.9f, 2.0f, 2.0f));
        Bevel.Draw(this, cover, new Color(1, 1, 1, 0.4f), new Color(0, 0, 0, 0.6f), 2);
        float mid = Size.X / 2 + 8;
        var left = new Rect2(26, 12, mid - 34, Size.Y - 28);
        var right = new Rect2(mid + 6, 12, Size.X - mid - 40, Size.Y - 28);
        var paper = Dark ? Color.FromHtml("#303030") : Color.FromHtml("#e6e6e2");
        foreach (var p in new[] { left, right })
        {
            DrawRect(p.Grow(1), new Color(0, 0, 0, 0.5f));
            DrawRect(p, paper);
            DrawRect(new Rect2(p.Position + new Vector2(8, 8), p.Size - new Vector2(16, 16)), Dark ? Color.FromHtml("#3a3a3a") : Color.FromHtml("#f2f2ee"));
        }
        // The spine and its rings.
        DrawRect(new Rect2(mid - 8, 6, 14, Size.Y - 14), Color.FromHtml("#5a5d5a"));
        for (int i = 0; i < 3; i++)
        {
            float y = 60 + i * 120;
            DrawRect(new Rect2(mid - 14, y, 26, 8), Color.FromHtml("#b8b8b8"));
            DrawRect(new Rect2(mid - 14, y, 26, 8), Colors.Black, filled: false, width: 1);
        }
    }
}

/// <summary>A coloured tab on the binder's edge with its word written downwards.</summary>
public partial class BookTab : DrawnButton
{
    public string Label { get; set; } = "";
    public Color Tint { get; set; }
    public bool Active { get; set; }

    public override void _Draw()
    {
        if (DrawSkin(Active)) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Active ? Tint.Lightened(0.15f) : Tint.Darkened(0.1f));
        Bevel.Draw(this, r, new Color(1, 1, 1, Active ? 0.1f : 0.45f), new Color(0, 0, 0, 0.5f), Active ? 1 : 2);
        var f = Look.CondensedBold;
        const int size = 13;
        float h = Label.Length * 13;
        float y = (Size.Y - h) / 2 + 11;
        foreach (char c in Label)
        {
            DrawString(f, new Vector2(0, y), c.ToString(), HorizontalAlignment.Center, Size.X, size, Color.FromHtml("#3a3a3a"));
            y += 13;
        }
    }
}

/// <summary>A card on a Catalog page (or in the Decal Browser): the picture and its name.</summary>
public partial class CatalogItem : DrawnButton
{
    public string Label { get; set; } = "";
    public Texture2D? Picture { get; set; }
    /// <summary>The picture drawn live while the part turns under the pointer (the original's look).</summary>
    public Texture2D? Live { get; set; }
    public bool Dark { get; set; }
    /// <summary>Uses left, shown in the corner (Decal Browser). 0 = none shown.</summary>
    public int Count { get; set; }
    /// <summary>With a skin: the original's card (flat grey, name in the small dark letters) or, for
    /// decals, the picture over its name in small white letters.</summary>
    public bool Classic { get; set; }

    public override void _Draw()
    {
        if (Classic && DrawClassic()) return;
        var r = new Rect2(Vector2.Zero, Size);
        if (!Dark)
        {
            DrawRect(r, Hovered ? Color.FromHtml("#c9c9c9") : Color.FromHtml("#b8b8b8"));
            Bevel.Draw(this, r, new Color(1, 1, 1, 0.6f), new Color(0, 0, 0, 0.35f));
        }
        else if (Hovered) DrawRect(r, new Color(1, 1, 1, 0.12f));
        var pic = Dark ? new Rect2(Size.X / 2 - 24, 4, 48, Size.Y - 24) : new Rect2(Size.X / 2 - 34, 3, 68, Size.Y - 16);
        if (Picture is not null)
        {
            var ts = Picture.GetSize();
            float s = Mathf.Min(pic.Size.X / ts.X, pic.Size.Y / ts.Y);
            var sz = ts * s;
            DrawTextureRect(Picture, new Rect2(pic.Position + (pic.Size - sz) / 2, sz), false);
        }
        var f = Look.CondensedBold;
        const int size = 10;
        float y = Size.Y - 4;
        var lines = Dark ? Label.Split(' ', 2) : [Label];
        if (lines.Length == 2 && f.GetStringSize(Label, HorizontalAlignment.Left, -1, size).X <= Size.X - 2) lines = [Label];
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            DrawString(f, new Vector2(0, y), lines[i], HorizontalAlignment.Center, Size.X, size, Dark ? Colors.White : Color.FromHtml("#2a2a2a"));
            y -= 10;
        }
        if (Count > 0) DrawString(f, new Vector2(3, 11), Count.ToString(), HorizontalAlignment.Left, -1, 10, Colors.White);
    }

    bool DrawClassic()
    {
        var font = UiSkin.Font(Dark ? "tiny" : "tinyblack");
        if (font is null) return false;
        int fs = UiSkin.FontSize(font);
        if (!Dark)
        {
            // Measured: a flat card with the part's name at the bottom, its letters' tops 39 below the card's (a name too
            // long for the card goes on two lines, the last one where a single line is, the first 9 above: "Front
            // Windshield" / "ChopTop"), the part's picture over it round the card's middle (see
            // Thumbnails.CatalogDistance), cut at the card's edges.
            var card = new Rect2(Vector2.Zero, Size);
            DrawRect(card, L.Orig.PartCardColor);
            var rows = Wrap(font, fs, Label, Size.X);
            for (int i = 0; i < rows.Count; i++)
                DrawString(font, new Vector2(0, Size.Y - fs + font.GetAscent(fs) - (rows.Count - 1 - i) * 9), rows[i], HorizontalAlignment.Center, Size.X, fs);
            if ((Live ?? Picture) is { } pic)
            {
                const float r = OpenGG.View3D.Thumbnails.CatalogRadius;
                var at = new Rect2(L.Orig.PartPictureMiddle - new Vector2(r, r), new Vector2(2 * r, 2 * r));
                var shown = at.Intersection(card);
                var ts = pic.GetSize();
                DrawTextureRectRegion(pic, shown, new Rect2((shown.Position - at.Position) / at.Size * ts, shown.Size / at.Size * ts));
            }
            return true;
        }
        // Decals: the picture in a 50 x 50 box, the name (one or two lines) under it.
        if (Picture is not null) Fit(Picture, new Rect2((Size.X - 50) / 2, 0, 50, 50));
        var words = Label.Split(' ');
        var lines = font.GetStringSize(Label, HorizontalAlignment.Left, -1, fs).X <= Size.X || words.Length < 2
            ? new[] { Label }
            : new[] { string.Join(' ', words[..(words.Length / 2 + (words.Length % 2))]), string.Join(' ', words[(words.Length / 2 + (words.Length % 2))..]) };
        float top = 58.5f - lines.Length * fs / 2f;
        for (int i = 0; i < lines.Length; i++)
            DrawString(font, new Vector2(0, top + i * fs + font.GetAscent(fs)), lines[i], HorizontalAlignment.Center, Size.X, fs);
        if (Count > 0) DrawString(font, new Vector2(2, font.GetAscent(fs)), Count.ToString(), HorizontalAlignment.Left, -1, fs);
        return true;
    }

    /// <summary>A name in lines that fit <paramref name="width"/>: as many words on a line as fit, the rest below.</summary>
    public static List<string> Wrap(Font font, int size, string text, float width)
    {
        var rows = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.GetStringSize(tryLine, HorizontalAlignment.Left, -1, size).X > width)
            {
                rows.Add(line);
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0 || rows.Count == 0) rows.Add(line);
        return rows;
    }

    void Fit(Texture2D pic, Rect2 box)
    {
        var ts = pic.GetSize();
        float s = Mathf.Min(box.Size.X / ts.X, box.Size.Y / ts.Y);
        var sz = ts * s;
        DrawTextureRect(pic, new Rect2(box.Position + (box.Size - sz) / 2, sz), false);
    }
}

/// <summary>"TURN PAGE" with an arrow, in a bottom corner of the binder.</summary>
public partial class PageTurn : DrawnButton
{
    public string Label { get; set; } = "TURN PAGE";
    public bool Left { get; set; }
    public bool Dark { get; set; }

    public override void _Draw()
    {
        if (DrawSkin(Down)) return;
        var ink = Dark ? (Hovered ? Colors.White : Color.FromHtml("#d8d8d8")) : (Hovered ? Colors.Black : Color.FromHtml("#3a3a3a"));
        float cy = Size.Y / 2;
        float ax = Left ? 2 : Size.X - 2;
        float dir = Left ? 1 : -1;
        DrawColoredPolygon([new Vector2(ax, cy), new Vector2(ax + dir * 10, cy - 5), new Vector2(ax + dir * 10, cy + 5)], ink);
        DrawLine(new Vector2(ax + dir * 10, cy), new Vector2(ax + dir * 22, cy), ink, 3);
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(Left ? 26 : 0, cy + 4), Label, Left ? HorizontalAlignment.Left : HorizontalAlignment.Right, Size.X - 26, 11, ink);
    }
}

/// <summary>The JunkYard's signpost: "Areas:" and three planks.</summary>
public partial class SignPost : Control
{
    public SignPost() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Size.X / 2 - 5, 20, 10, Size.Y - 20), Color.FromHtml("#6a4a2c"));
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(0, 18), "Areas:", HorizontalAlignment.Center, Size.X, 15, Look.White);
    }
}

/// <summary>A plank of the signpost pointing to an area of the yard.</summary>
public partial class SignPlank : DrawnButton
{
    public string Label { get; set; } = "";
    public bool Active { get; set; }

    public override void _Draw()
    {
        // The skin's planks are on the empty screen; a pressed one comes from the down picture.
        if (DrawSkin(Down ? true : null)) return;
        var w = Size.X;
        var h = Size.Y;
        Vector2[] shape = [new(0, 2), new(w - 12, 2), new(w, h / 2), new(w - 12, h - 2), new(0, h - 2)];
        DrawColoredPolygon(shape, Active ? Color.FromHtml("#e0c080") : Hovered ? Color.FromHtml("#d8b878") : Color.FromHtml("#c8a466"));
        DrawPolyline([.. shape, shape[0]], Color.FromHtml("#3a2410"), 1);
        var f = Look.CondensedBold;
        DrawString(f, new Vector2(6, h / 2 + 5), Label, HorizontalAlignment.Left, w - 18, 13, Color.FromHtml("#2a1a08"));
    }
}

/// <summary>A dark frame around a 3D view.</summary>
public partial class ViewFrame : Control
{
    public ViewFrame() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black, filled: false, width: 2);
}

/// <summary>An Auction value: green figures in a black box, with "&gt;&gt;" when you lead.</summary>
public partial class AuctionValue : Control
{
    public string Value { get; set; } = "";
    public bool Lead { get; set; }

    public AuctionValue() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>With a skin: the money figures, right-aligned at this global x and top.</summary>
    public Vector2? SkinAt { get; set; }
    /// <summary>With a skin: where the ">>" goes while you lead (global).</summary>
    public Vector2? SkinLeadAt { get; set; }

    /// <summary>The original's ">>": two chevrons, white on a black edge, row by row (measured on a capture).</summary>
    static readonly string[] Chevron = ["##.....", "..###..", "....###", "....###", ".###...", "##....."];

    public override void _Draw()
    {
        if (SkinAt is { } at && UiSkin.Font("cash") is { } cash)
        {
            int fs = UiSkin.FontSize(cash);
            var local = at - GetGlobalTransform().Origin;
            float w = cash.GetStringSize(Value, HorizontalAlignment.Left, -1, fs).X;
            DrawString(cash, new Vector2(local.X - w, local.Y + cash.GetAscent(fs)), Value, HorizontalAlignment.Left, -1, fs);
            if (Lead && SkinLeadAt is { } leadAt)
            {
                var o = leadAt - GetGlobalTransform().Origin;
                for (int pass = 0; pass < 2; pass++)
                    for (int c = 0; c < 2; c++)
                        for (int row = 0; row < Chevron.Length; row++)
                            for (int col = 0; col < Chevron[row].Length; col++)
                            {
                                if (Chevron[row][col] != '#') continue;
                                var p = o + new Vector2(c * 9 + col, row);
                                if (pass == 0) DrawRect(new Rect2(p - Vector2.One, new Vector2(3, 3)), Colors.Black);
                                else DrawRect(new Rect2(p, Vector2.One), Colors.White);
                            }
            }
            return;
        }
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Color.FromHtml("#0c0c0c"));
        Bevel.Draw(this, r, new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.18f), 2);
        var f = Look.CondensedBold;
        const int size = 18;
        float y = (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size);
        if (Lead) DrawString(f, new Vector2(-24, y), ">>", HorizontalAlignment.Left, -1, size, Colors.White);
        DrawString(f, new Vector2(0, y), Value, HorizontalAlignment.Right, Size.X - 6, size, Look.MoneyGreen);
    }
}

/// <summary>With a skin: figures in one of its fonts, centred across the control, the top of its letters at the top.</summary>
public partial class SkinFigures : Control
{
    public string Value { get; set; } = "";
    public string FontId { get; set; } = "cash";

    public SkinFigures() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (UiSkin.Font(FontId) is not { } f) return;
        int fs = UiSkin.FontSize(f);
        float w = f.GetStringSize(Value, HorizontalAlignment.Left, -1, fs).X;
        DrawString(f, new Vector2(Mathf.Round((Size.X - w) / 2), f.GetAscent(fs)), Value, HorizontalAlignment.Left, -1, fs);
    }
}

/// <summary>The gold bar under the Car Lot's view with a marker under the chosen car.</summary>
public partial class LotBar : Control
{
    /// <summary>Where the marker goes along the bar (none with the lot empty).</summary>
    public float? MarkerX { get; set; }

    public LotBar() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>With a skin the bar is on the screen: only the marker (a gold triangle) is drawn.</summary>
    public bool Skinned { get; set; }

    public override void _Draw()
    {
        if (Skinned)
        {
            if (MarkerX is not { } sx) return;
            // Measured: 28 pixels across and 16 high, its point on the top edge.
            float mx = Mathf.Clamp(sx, 14, Size.X - 14);
            DrawColoredPolygon([new Vector2(mx, 0), new Vector2(mx - 14, 16), new Vector2(mx + 14, 16)], Color.FromHtml("#ce9b39"));
            return;
        }
        DrawRect(new Rect2(6, 6, Size.X - 12, 5), Color.FromHtml("#b58a31"));
        if (MarkerX is not { } x) return;
        x = Mathf.Clamp(x, 14, Size.X - 14);
        DrawColoredPolygon([new Vector2(x, 0), new Vector2(x - 8, 10), new Vector2(x + 8, 10)], Color.FromHtml("#d6aa4a"));
    }
}

/// <summary>The decal as it will go on, in the Decal Browser.</summary>
public partial class DecalPreview : Control
{
    public Texture2D? Picture { get; set; }
    public float Angle { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public Color Tint { get; set; } = Colors.White;
    public float SizeFactor { get; set; } = 1;
    /// <summary>The original's look: the picture fills the control, on the panel's own box, whatever the size.</summary>
    public bool Classic { get; set; }

    public DecalPreview() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (!Classic) DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#3a3a3a"));
        // The original smooths the decal as it scales it up (matched on the captures).
        else TextureFilter = TextureFilterEnum.Linear;
        if (Picture is null) return;
        var ts = Picture.GetSize();
        float s = Classic ? 1 : Mathf.Min(Size.X / ts.X, Size.Y / ts.Y) * 0.7f * SizeFactor;
        var sz = Classic ? Size : ts * s;
        DrawSetTransform(Size / 2, -Angle, new Vector2(FlipX ? -1 : 1, FlipY ? -1 : 1));
        DrawTextureRect(Picture, new Rect2(-sz / 2, sz), false, Tint);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>A decal in the original's Decal Browser: its picture at 48 x 48, the uses left in white on a black patch
/// at the picture's corner, the name 7 pixels under it in the small white letters, centred.</summary>
public partial class BrowserDecal : DrawnButton
{
    public string Label { get; set; } = "";
    public Texture2D? Picture { get; set; }
    public int Count { get; set; }

    /// <summary>Where the picture sits in the cell (the cell is as wide as the pitch, 56).</summary>
    public static readonly Rect2 Icon = new(4, 0, 48, 48);

    public override void _Draw()
    {
        if (Picture is not null) DrawTextureRect(Picture, Icon, false);
        if (UiSkin.Font("tiny") is not { } font) return;
        int fs = UiSkin.FontSize(font);
        var words = Label.Split(' ');
        var lines = font.GetStringSize(Label, HorizontalAlignment.Left, -1, fs).X <= Size.X || words.Length < 2
            ? new[] { Label }
            : new[] { string.Join(' ', words[..((words.Length + 1) / 2)]), string.Join(' ', words[((words.Length + 1) / 2)..]) };
        for (int i = 0; i < lines.Length; i++)
            DrawString(font, new Vector2(0, Icon.End.Y + 7 + i * fs + font.GetAscent(fs)), lines[i], HorizontalAlignment.Center, Size.X, fs);
        var count = Count.ToString();
        float w = font.GetStringSize(count, HorizontalAlignment.Left, -1, fs).X;
        DrawRect(new Rect2(Icon.Position.X, 0, w + 1, 9), Colors.Black);
        DrawString(font, new Vector2(Icon.Position.X + 1, font.GetAscent(fs)), count, HorizontalAlignment.Left, -1, fs);
    }
}

// ---- the sign-in screen: pale blue paper, a ruled table, outlined lettering ----------------------------

/// <summary>Pale paper with our name in big blue letters.</summary>
public partial class SignInPaper : Control
{
    public SignInPaper() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#eef0fa"));
        var blue = Color.FromHtml("#4a5ab8");
        DrawSetTransformMatrix(new Transform2D(new Vector2(1, 0), new Vector2(-0.16f, 1), new Vector2(18, 86)));
        DrawString(Look.Impact, Vector2.Zero, "OpenGG", HorizontalAlignment.Left, -1, 64, blue);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawString(Look.Heavy, new Vector2(248, 84), "Garage", HorizontalAlignment.Left, -1, 38, blue);
        DrawString(Look.Plain, new Vector2(12, 116), "SIGN IN:", HorizontalAlignment.Left, -1, 17, blue);
        DrawString(Look.Plain, new Vector2(112, 115), "Choose an existing mechanic or create a new one.", HorizontalAlignment.Left, -1, 10, blue);
        DrawString(Look.Plain, new Vector2(8, 472), "OpenGG is an open-source fan project.", HorizontalAlignment.Left, -1, 10, blue);
    }
}

/// <summary>The mechanics table: blue rules, a pale blue COMMANDS column.</summary>
public partial class SignInTable : Control
{
    public float[] Columns { get; set; } = [];
    public int Rows { get; set; } = 9;

    public SignInTable() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var blue = Color.FromHtml("#7a86c8");
        float top = 126, head = 22, row = 24;
        float bottom = top + head + Rows * row + 4;
        float l = Columns[0], r = Columns[^1];
        DrawRect(new Rect2(l, top, r - l, bottom - top), Colors.White);
        DrawRect(new Rect2(l, top + head, Columns[1] - l, bottom - top - head), Color.FromHtml("#dfe3f6"));
        DrawRect(new Rect2(l, top, r - l, head), Color.FromHtml("#dfe3f6"));
        DrawRect(new Rect2(l, top, r - l, bottom - top), blue, filled: false, width: 2);
        DrawLine(new Vector2(l, top + head), new Vector2(r, top + head), blue, 2);
        for (int i = 1; i < Columns.Length - 1; i++) DrawLine(new Vector2(Columns[i], top), new Vector2(Columns[i], bottom), blue, 1);
    }
}

/// <summary>A sign-in button: blue outlined lettering (DELETE, NEW, a mechanic's name).</summary>
public partial class OutlineButton : DrawnButton
{
    public string Label { get; set; } = "";
    /// <summary>Plain blue text instead of the hollow letters.</summary>
    public bool Plain { get; set; }
    public bool Small { get; set; }

    public override void _Draw()
    {
        var blue = Color.FromHtml("#4a5ab8");
        var f = Look.Plain;
        int size = Small ? 10 : Plain ? 14 : 13;
        float y = (Size.Y - (f.GetAscent(size) + f.GetDescent(size))) / 2 + f.GetAscent(size);
        if (Hovered) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.3f, 0.36f, 0.72f, 0.12f));
        if (Plain || Small)
            DrawString(f, new Vector2(0, y), Label, HorizontalAlignment.Center, Size.X, size, Hovered ? Color.FromHtml("#2a3a9a") : blue);
        else
        {
            DrawRect(new Rect2(Vector2.Zero, Size), blue, filled: false, width: 1);
            DrawStringOutline(f, new Vector2(0, y), Label, HorizontalAlignment.Center, Size.X, size, 2, blue);
            DrawString(f, new Vector2(0, y), Label, HorizontalAlignment.Center, Size.X, size, Hovered ? Color.FromHtml("#dfe3f6") : Colors.White);
        }
    }
}

/// <summary>The OPTIONS box of the sign-in screen.</summary>
public partial class OptionsBox : Control
{
    public OptionsBox() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var blue = Color.FromHtml("#7a86c8");
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.White);
        DrawRect(new Rect2(0, 0, Size.X, 22), Color.FromHtml("#dfe3f6"));
        DrawRect(new Rect2(Vector2.Zero, Size), blue, filled: false, width: 2);
        DrawString(Look.Plain, new Vector2(0, 16), "OPTIONS", HorizontalAlignment.Center, Size.X, 10, Color.FromHtml("#4a5ab8"));
    }
}

/// <summary>A sign-in dialog ("CREATE NEW MECHANIC").</summary>
public partial class SignInBox : Control
{
    public string Title { get; set; } = "";
    /// <summary>With a skin: the original's box (the title goes in its white field).</summary>
    public Texture2D? Picture { get; set; }

    public SignInBox() => MouseFilter = MouseFilterEnum.Stop;

    public override void _Draw()
    {
        if (Picture is not null)
        {
            DrawTexture(Picture, Vector2.Zero);
            if (UiSkin.Font("signin") is { } f)
            {
                int fs = UiSkin.FontSize(f);
                DrawString(f, new Vector2(0, 7 + f.GetAscent(fs) - 4), Title, HorizontalAlignment.Center, Size.X, fs);
            }
            return;
        }
        var blue = Color.FromHtml("#7a86c8");
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#eef0fa"));
        DrawRect(new Rect2(Vector2.Zero, Size), blue, filled: false, width: 2);
        DrawRect(new Rect2(24, 6, Size.X - 48, 22), Colors.White);
        DrawRect(new Rect2(24, 6, Size.X - 48, 22), blue, filled: false, width: 1);
        DrawString(Look.Plain, new Vector2(0, 22), Title, HorizontalAlignment.Center, Size.X, 13, Color.FromHtml("#4a5ab8"));
        DrawLine(new Vector2(20, 127), new Vector2(Size.X - 20, 127), blue, 1);
    }
}

/// <summary>Our credits page.</summary>
public partial class CreditsPage : Control
{
    public CreditsPage() => MouseFilter = MouseFilterEnum.Stop;

    public override void _Draw()
    {
        DrawTextureRect(Look.MetalTexture, new Rect2(Vector2.Zero, Size), tile: true);
        var f = Look.CondensedBold;
        void Line(float y, string text, int size, Color c) => DrawString(f, new Vector2(40, y), text, HorizontalAlignment.Left, Size.X - 80, size, c);
        DrawString(Look.Heavy, new Vector2(40, 60), "OpenGG", HorizontalAlignment.Left, -1, 30, Look.TitleLight);
        Line(100, "An open-source engine that plays Gearhead Garage (1999) with the game files you own.", 14, Look.Orange);
        Line(140, "Not affiliated with Ratloop, Mekada, Head Games, Activision or Snap-on.", 13, Look.White);
        Line(165, "OpenGG contains no files from the original game. Your copy's cars, jobs and decals", 13, Look.White);
        Line(185, "are read at run time; everything else (the ai_ models, textures, sounds and portraits)", 13, Look.White);
        Line(205, "is placeholder content made for OpenGG.", 13, Look.White);
        Line(250, "Engine: Godot 4.7 (MIT licence).", 13, Look.Muted);
    }
}
