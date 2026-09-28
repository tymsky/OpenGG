using System;
using Godot;

namespace OpenGG.Ui;

/// <summary>
/// A decal as the original lays it on a car (measured on its captures and paint pictures): flat on the screen, upright
/// whatever the car's angle, turned and flipped as the Decal Browser shows it, at its picture's own pixels times a half,
/// one or one and a half (smaller, Normal, BIGGER), its middle on the pointer. Its ground (clear, or pure black as the
/// original's pictures have it) is left out.
/// </summary>
public sealed class DecalFrame
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Row by row; null where the picture's ground is.</summary>
    public Color?[] Pixels { get; }

    DecalFrame(int w, int h, Color?[] px) => (Width, Height, Pixels) = (w, h, px);

    /// <summary>The size options' scales, measured on the aiming preview: a Star of 54 x 51 pixels came out 23 x 21,
    /// 51 x 49 and 81 x 77 (its every other pixel shown), the U.S.A. flag's 32 x 17 15 x 7, 31 x 17 and 47 x 25.</summary>
    public static float Scale(int sizeIndex) => sizeIndex switch { 0 => 1.5f, 2 => 0.5f, _ => 1f };

    /// <param name="angle">Turned so far in the browser (a quarter turn a click; positive = to the left).</param>
    public static DecalFrame? Build(Texture2D? picture, float angle, bool flipX, bool flipY, float scale, Color tint)
    {
        if (picture?.GetImage() is not { } img || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        int w0 = img.GetWidth(), h0 = img.GetHeight();
        int quarter = ((int)Mathf.Round(angle / (Mathf.Pi / 2)) % 4 + 4) % 4;
        int tw = quarter % 2 == 0 ? w0 : h0, th = quarter % 2 == 0 ? h0 : w0;
        int w = Math.Max(1, (int)Math.Round(tw * scale)), h = Math.Max(1, (int)Math.Round(th * scale));
        var px = new Color?[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                // The frame's pixel in the turned picture's pixels, from its middle; back through the turn, then the flip
                // (the browser's preview draws the flipped picture turned).
                double x = (i + 0.5) / scale - tw / 2.0, y = (j + 0.5) / scale - th / 2.0;
                (x, y) = quarter switch { 1 => (-y, x), 2 => (-x, -y), 3 => (y, -x), _ => (x, y) };
                if (flipX) x = -x;
                if (flipY) y = -y;
                int sx = (int)Math.Floor(x + w0 / 2.0), sy = (int)Math.Floor(y + h0 / 2.0);
                if (sx < 0 || sy < 0 || sx >= w0 || sy >= h0) continue;
                var c = img.GetPixel(sx, sy);
                if (c.A < 0.5f || Mathf.Max(c.R, Mathf.Max(c.G, c.B)) < 0.012f) continue;
                px[j * w + i] = new Color(c.R * tint.R, c.G * tint.G, c.B * tint.B);
            }
        return new DecalFrame(w, h, px);
    }

    /// <summary>Where the frame's top left corner goes for the pointer at <paramref name="pointer"/>.</summary>
    public Vector2 Origin(Vector2 pointer) => new(Mathf.Floor(pointer.X - Width / 2f), Mathf.Floor(pointer.Y - Height / 2f));
}

/// <summary>
/// The decal being aimed, drawn over the WorkShop's view where it will go. The original's look (measured): only every
/// other pixel of it, both ways, counted from its corner (odd columns of odd rows), over everything behind, the car or
/// not, and no pointer. Our own look: the whole picture, see-through.
/// </summary>
public partial class DecalAim : Control
{
    ImageTexture? texture;
    DecalFrame? frame;

    public DecalAim()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    public DecalFrame? Frame => frame;

    public void SetFrame(DecalFrame? f, bool dots)
    {
        frame = f;
        texture = null;
        if (f is null) return;
        var img = Image.CreateEmpty(f.Width, f.Height, false, Image.Format.Rgba8);
        for (int j = 0; j < f.Height; j++)
            for (int i = 0; i < f.Width; i++)
            {
                if (f.Pixels[j * f.Width + i] is not { } c) continue;
                if (dots && (i % 2 == 0 || j % 2 == 0)) continue;
                img.SetPixel(i, j, dots ? c : c with { A = 0.55f });
            }
        texture = ImageTexture.CreateFromImage(img);
        Size = new Vector2(f.Width, f.Height);
        QueueRedraw();
    }

    public void Place(Vector2 pointer)
    {
        if (frame is null) return;
        Position = frame.Origin(pointer);
        Visible = true;
    }

    public override void _Draw()
    {
        if (texture is not null) DrawTexture(texture, Vector2.Zero);
    }
}
