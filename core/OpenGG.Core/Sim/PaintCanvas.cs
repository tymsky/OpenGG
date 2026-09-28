// A car's paint as a picture, as in the original: every paintable panel has its own square of a
// 256 x 256 image (the "paint" faces' UVs point into it), so paint can be sprayed on freehand.

using System.IO.Compression;

namespace OpenGG.Core.Sim;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length < 6) return new Rgb(128, 128, 128);
        return new Rgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    public string Hex => $"#{R:x2}{G:x2}{B:x2}";
}

/// <summary>One pixel of a decal as it lands in the paint picture (<see cref="Game.StampDecal"/>): where, in what colour,
/// and the place of the part it is on (null on the car's fixed body).</summary>
public readonly record struct DecalTexel(int X, int Y, Rgb Color, string? SlotId);

public sealed class PaintCanvas
{
    public const int Size = 256;

    /// <summary>RGB, row by row, top row first (v = 0 at the top, as the UVs are stored).</summary>
    public byte[] Pixels { get; } = new byte[Size * Size * 3];

    /// <summary>Bumped on every change, so a renderer knows when to upload the picture again.</summary>
    public int Version { get; private set; }

    public static PaintCanvas Filled(Rgb c)
    {
        var p = new PaintCanvas();
        p.FillRect(0, 0, Size, Size, c);
        return p;
    }

    /// <summary>Fills whole pixels from (x0, y0) up to but not including (x1, y1).</summary>
    public void FillRect(int x0, int y0, int x1, int y1, Rgb c)
    {
        x0 = Math.Clamp(x0, 0, Size);
        x1 = Math.Clamp(x1, 0, Size);
        y0 = Math.Clamp(y0, 0, Size);
        y1 = Math.Clamp(y1, 0, Size);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * Size + x) * 3;
                Pixels[i] = c.R;
                Pixels[i + 1] = c.G;
                Pixels[i + 2] = c.B;
            }
        Version++;
    }

    /// <summary>Fills a UV rectangle (a panel's square of the picture).</summary>
    public void FillUv(double u0, double v0, double u1, double v1, Rgb c) =>
        FillRect((int)Math.Floor(u0 * Size), (int)Math.Floor(v0 * Size), (int)Math.Ceiling(u1 * Size), (int)Math.Ceiling(v1 * Size), c);

    /// <summary>A square dab of paint <paramref name="side"/> pixels wide, centred on a UV point.
    /// Kept inside <paramref name="clip"/> (the panel's square) so paint doesn't bleed onto other panels.</summary>
    public void Dab(double u, double v, int side, Rgb c, (double U0, double V0, double U1, double V1)? clip = null)
    {
        int cx = (int)Math.Floor(u * Size), cy = (int)Math.Floor(v * Size);
        int x0 = cx - side / 2, y0 = cy - side / 2;
        int x1 = x0 + Math.Max(1, side), y1 = y0 + Math.Max(1, side);
        if (clip is { } k)
        {
            x0 = Math.Max(x0, (int)Math.Floor(k.U0 * Size));
            y0 = Math.Max(y0, (int)Math.Floor(k.V0 * Size));
            x1 = Math.Min(x1, (int)Math.Ceiling(k.U1 * Size));
            y1 = Math.Min(y1, (int)Math.Ceiling(k.V1 * Size));
        }
        FillRect(x0, y0, x1, y1, c);
    }

    /// <summary>Single pixels (a decal painted in, one pixel of the picture at a time); those outside are skipped.</summary>
    public void Put(IEnumerable<(int X, int Y, Rgb C)> pixels)
    {
        foreach (var (x, y, c) in pixels)
        {
            if ((uint)x >= Size || (uint)y >= Size) continue;
            int i = (y * Size + x) * 3;
            Pixels[i] = c.R;
            Pixels[i + 1] = c.G;
            Pixels[i + 2] = c.B;
        }
        Version++;
    }

    public Rgb At(int x, int y)
    {
        int i = (Math.Clamp(y, 0, Size - 1) * Size + Math.Clamp(x, 0, Size - 1)) * 3;
        return new Rgb(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    /// <summary>Compact text for the save file (deflated, base64).</summary>
    public string Encode()
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(Pixels);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static PaintCanvas? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            using var z = new ZLibStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
            var p = new PaintCanvas();
            int read = 0;
            while (read < p.Pixels.Length)
            {
                int n = z.Read(p.Pixels, read, p.Pixels.Length - read);
                if (n == 0) break;
                read += n;
            }
            return read == p.Pixels.Length ? p : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
