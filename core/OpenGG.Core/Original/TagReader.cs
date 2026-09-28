// Reader for the original game's tagged data files (.car, .jpk, .dpk, .mek).
// Format: docs/formats/tagged-files.md. Written from the data files alone; no code of the
// original game was looked at.

using System.Text;
using OpenGG.Core.Content;

namespace OpenGG.Core.Original;

public enum TagType { Raw = 0, Int = 1, Float = 2, String = 3, Node = 4 }

/// <summary>A node's id, type and where its payload is.</summary>
public readonly record struct TagHeader(int Id, TagType Type, long Start, int Length)
{
    public long End => Start + Length;
}

/// <summary>Walks a tagged file without loading more than it is asked for.</summary>
public sealed class TagReader : IDisposable
{
    readonly Stream stream;
    readonly BinaryReader reader;

    public TagReader(Stream stream)
    {
        this.stream = stream;
        reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
    }

    /// <param name="inMemory">Read the whole file at once (fast for full loads); otherwise seek on disk.</param>
    public static TagReader Open(string path, bool inMemory = false) =>
        new(inMemory ? new MemoryStream(File.ReadAllBytes(path), writable: false) : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16));

    public void Dispose() => reader.Dispose();

    public TagHeader Root() => Header(0, stream.Length) ?? throw new InvalidDataException("Empty file.");

    TagHeader? Header(long pos, long end)
    {
        if (pos + 8 > end) return null;
        stream.Position = pos;
        uint tag = reader.ReadUInt32();
        int len = reader.ReadInt32();
        if (len < 0 || pos + 8 + len > end) throw new InvalidDataException($"Broken node at {pos} (length {len}).");
        return new TagHeader((int)(tag & 0xffff), (TagType)(tag >> 29), pos + 8, len);
    }

    public IEnumerable<TagHeader> Children(TagHeader node)
    {
        long pos = node.Start;
        while (Header(pos, node.End) is { } h)
        {
            yield return h;
            pos = h.End;
        }
    }

    public TagHeader? Child(TagHeader node, int id)
    {
        foreach (var c in Children(node))
            if (c.Id == id) return c;
        return null;
    }

    public int Int(TagHeader h)
    {
        stream.Position = h.Start;
        return h.Length >= 4 ? reader.ReadInt32() : 0;
    }

    /// <summary>A float, or NaN for the "no value" patterns the tool wrote (CD CD CD CD, NaN).</summary>
    public float Float(TagHeader h)
    {
        stream.Position = h.Start;
        return h.Length >= 4 ? Clean(reader.ReadUInt32()) : float.NaN;
    }

    static float Clean(uint bits)
    {
        if (bits == 0xCDCDCDCD) return float.NaN;
        float f = BitConverter.UInt32BitsToSingle(bits);
        return float.IsFinite(f) ? f : float.NaN;
    }

    public string String(TagHeader h)
    {
        stream.Position = h.Start;
        return Encoding.Latin1.GetString(reader.ReadBytes(h.Length));
    }

    public byte[] Bytes(TagHeader h)
    {
        stream.Position = h.Start;
        return reader.ReadBytes(h.Length);
    }

    public int[] Ints(TagHeader h)
    {
        stream.Position = h.Start;
        var a = new int[h.Length / 4];
        for (int i = 0; i < a.Length; i++) a[i] = reader.ReadInt32();
        return a;
    }

    public float[] Floats(TagHeader h)
    {
        stream.Position = h.Start;
        var a = new float[h.Length / 4];
        for (int i = 0; i < a.Length; i++) a[i] = Clean(reader.ReadUInt32());
        return a;
    }

    public (float X, float Y, float Z) Vec(TagHeader h)
    {
        var f = Floats(h);
        return f.Length >= 3 ? (f[0], f[1], f[2]) : (float.NaN, float.NaN, float.NaN);
    }
}

/// <summary>
/// The original's space is left-handed (Direct3D): +Y up, front at -X, left at -Z, about 1.8 units
/// per inch. OpenGG uses meters, front at +X, right at +Z (right-handed). Negating X does both.
/// </summary>
public static class OrigSpace
{
    public const double MetersPerUnit = 0.0254 / 1.8;

    public static Vec3 Point(float x, float y, float z) => new(-x * MetersPerUnit, y * MetersPerUnit, z * MetersPerUnit);
    public static Vec3 Point((float X, float Y, float Z) v) => Point(v.X, v.Y, v.Z);
    public static Vec3 Direction(float x, float y, float z) => new Vec3(-x, y, z).Normalized();

    /// <summary>Rotation rows (local axes in car space), mirrored the same way: R' = S R S, S = diag(-1, 1, 1).</summary>
    public static Vec3[] Rows(Vec3 r0, Vec3 r1, Vec3 r2) =>
    [
        new(r0.X, -r0.Y, -r0.Z),
        new(-r1.X, r1.Y, r1.Z),
        new(-r2.X, r2.Y, r2.Z),
    ];
}

/// <summary>A decoded picture, 8 bits per channel, RGBA, top row first.</summary>
public sealed class OrigImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Rgba { get; init; } = [];

    /// <summary>Decodes an image node (1000 width, 1001 height, 1002 bpp, 1003 pixels, 1004 palette).</summary>
    public static OrigImage? Read(TagReader r, TagHeader node, bool blackIsTransparent = false)
    {
        int w = 0, h = 0, bpp = 0;
        byte[]? pixels = null, palette = null;
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1000: w = r.Int(c); break;
                case 1001: h = r.Int(c); break;
                case 1002: bpp = r.Int(c); break;
                case 1003: pixels = r.Bytes(c); break;
                case 1004: palette = r.Bytes(c); break;
            }
        if (w <= 0 || h <= 0 || pixels is null || pixels.Length < w * h * (bpp / 8)) return null;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte R, G, B, A = 255;
            switch (bpp)
            {
                case 8:
                    int p = pixels[i] * 3;
                    if (palette is null || p + 2 >= palette.Length) { R = G = B = pixels[i]; }
                    else { R = palette[p]; G = palette[p + 1]; B = palette[p + 2]; }
                    break;
                case 16:
                    int v = pixels[i * 2] | (pixels[i * 2 + 1] << 8);
                    R = (byte)(((v >> 11) & 31) * 255 / 31);
                    G = (byte)(((v >> 5) & 63) * 255 / 63);
                    B = (byte)((v & 31) * 255 / 31);
                    break;
                case 24:
                    B = pixels[i * 3];
                    G = pixels[i * 3 + 1];
                    R = pixels[i * 3 + 2];
                    break;
                case 32:
                    B = pixels[i * 4];
                    G = pixels[i * 4 + 1];
                    R = pixels[i * 4 + 2];
                    A = pixels[i * 4 + 3];
                    break;
                default:
                    return null;
            }
            if (blackIsTransparent && R == 0 && G == 0 && B == 0) A = 0;
            rgba[i * 4] = R;
            rgba[i * 4 + 1] = G;
            rgba[i * 4 + 2] = B;
            rgba[i * 4 + 3] = A;
        }
        return new OrigImage { Width = w, Height = h, Rgba = rgba };
    }
}
