// TGA pictures, as the original keeps its 2D art in its archives: true colour, 24 or 32 bits, plain or run-length
// packed, stored from the bottom row up unless the header says otherwise.

namespace OpenGG.Core.Original;

public static class TgaFile
{
    public static OrigImage Read(byte[] d)
    {
        if (d.Length < 18) throw new InvalidDataException("Not a TGA picture (too short).");
        int idLength = d[0], colorMapType = d[1], type = d[2];
        int width = d[12] | d[13] << 8, height = d[14] | d[15] << 8, bpp = d[16], descriptor = d[17];
        if (colorMapType != 0 || type is not (2 or 10) || bpp is not (24 or 32) || width == 0 || height == 0)
            throw new InvalidDataException($"Unsupported TGA picture (type {type}, {bpp} bits).");
        int bytes = bpp / 8, pos = 18 + idLength, n = width * height;
        var pixels = new byte[n * bytes];
        if (type == 2)
        {
            if (pos + pixels.Length > d.Length) throw new InvalidDataException("The TGA picture is cut short.");
            Array.Copy(d, pos, pixels, 0, pixels.Length);
        }
        else
            for (int i = 0; i < n;)
            {
                if (pos >= d.Length) throw new InvalidDataException("The TGA picture is cut short.");
                int head = d[pos++], count = (head & 0x7F) + 1;
                bool run = (head & 0x80) != 0;
                for (int k = 0; k < count && i < n; k++, i++)
                {
                    int from = run ? pos : pos + k * bytes;
                    if (from + bytes > d.Length) throw new InvalidDataException("The TGA picture is cut short.");
                    Array.Copy(d, from, pixels, i * bytes, bytes);
                }
                pos += run ? bytes : count * bytes;
            }
        bool topFirst = (descriptor & 0x20) != 0;
        var rgba = new byte[n * 4];
        for (int y = 0; y < height; y++)
        {
            int row = topFirst ? y : height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                int s = (row * width + x) * bytes, o = (y * width + x) * 4;
                rgba[o] = pixels[s + 2];
                rgba[o + 1] = pixels[s + 1];
                rgba[o + 2] = pixels[s];
                rgba[o + 3] = bytes == 4 ? pixels[s + 3] : (byte)255;
            }
        }
        return new OrigImage { Width = width, Height = height, Rgba = rgba };
    }
}
