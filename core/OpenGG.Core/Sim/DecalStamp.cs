namespace OpenGG.Core.Sim;

/// <summary>One pixel of a decal as it lies on the screen: where it lands in the paint picture (in the picture's pixels,
/// not rounded), in what colour, on which part (null: the car's fixed body) and which of the car's painted surfaces
/// (a number the view gives each: pixels on different surfaces are never filled between).</summary>
public readonly record struct DecalSample(double X, double Y, Rgb Color, string? SlotId, int Surface);

/// <summary>Where a decal laid on the screen goes in the car's paint picture (see <see cref="Game.StampDecal"/>).</summary>
public static class DecalStamp
{
    /// <summary>How far apart, in the paint picture's pixels, neighbouring screen pixels may land and still have the
    /// pixels between them filled; farther apart they are taken for pieces of the surface laid out apart in the
    /// picture.</summary>
    public const double MaxGap = 12;

    /// <summary>
    /// The paint picture's pixels a decal paints, from its screen pixels (<paramref name="grid"/>, row by row,
    /// <paramref name="width"/> across; null where it is clear or lands on nothing that takes paint). The original paints
    /// only the pixels its screen pixels land on, so where the paint picture is finer than the screen (a panel at a slant,
    /// a small one drawn big) the paint shows between them in dots. OpenGG also fills what lies between neighbouring
    /// screen pixels on the same surface, each pixel with the colour of the nearest of them: a difference on purpose.
    /// </summary>
    public static List<DecalTexel> Texels(int width, int height, IReadOnlyList<DecalSample?> grid)
    {
        var texels = new Dictionary<(int, int), DecalTexel>();
        foreach (var g in grid)
            if (g is { } s)
                texels[((int)Math.Floor(s.X), (int)Math.Floor(s.Y))] = new DecalTexel((int)Math.Floor(s.X), (int)Math.Floor(s.Y), s.Color, s.SlotId);
        for (int j = 0; j + 1 < height; j++)
            for (int i = 0; i + 1 < width; i++)
            {
                if (grid[j * width + i] is not { } a || grid[j * width + i + 1] is not { } b
                    || grid[(j + 1) * width + i] is not { } c || grid[(j + 1) * width + i + 1] is not { } d)
                    continue;
                if (a.Surface != b.Surface || a.Surface != c.Surface || a.Surface != d.Surface) continue;
                if (Gap(a, b) > MaxGap || Gap(a, c) > MaxGap || Gap(b, d) > MaxGap || Gap(c, d) > MaxGap || Gap(a, d) > MaxGap) continue;
                Fill(texels, a, b, d);
                Fill(texels, a, d, c);
            }
        return [.. texels.Values];
    }

    static double Gap(DecalSample p, DecalSample q) => Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));

    /// <summary>The picture's pixels whose middles lie in the triangle p, q, r, each in the colour of the nearest corner
    /// (the pixels the screen's own land on are kept as they are).</summary>
    static void Fill(Dictionary<(int, int), DecalTexel> texels, DecalSample p, DecalSample q, DecalSample r)
    {
        double det = (q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y);
        if (Math.Abs(det) < 1e-9) return;
        int x0 = (int)Math.Floor(Math.Min(p.X, Math.Min(q.X, r.X))), x1 = (int)Math.Ceiling(Math.Max(p.X, Math.Max(q.X, r.X)));
        int y0 = (int)Math.Floor(Math.Min(p.Y, Math.Min(q.Y, r.Y))), y1 = (int)Math.Ceiling(Math.Max(p.Y, Math.Max(q.Y, r.Y)));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                double wq = ((cx - p.X) * (r.Y - p.Y) - (r.X - p.X) * (cy - p.Y)) / det;
                double wr = ((q.X - p.X) * (cy - p.Y) - (cx - p.X) * (q.Y - p.Y)) / det;
                double wp = 1 - wq - wr;
                const double eps = -1e-9;
                if (wp < eps || wq < eps || wr < eps || texels.ContainsKey((x, y))) continue;
                var near = wp >= wq && wp >= wr ? p : wq >= wr ? q : r;
                texels[(x, y)] = new DecalTexel(x, y, near.Color, near.SlotId);
            }
    }
}
