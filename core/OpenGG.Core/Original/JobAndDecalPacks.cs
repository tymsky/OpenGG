// The original's job packs (.jpk) and decal packs (.dpk).
// Layout: docs/formats/tagged-files.md.

namespace OpenGG.Core.Original;

public sealed record OrigStat(bool Region, int Target, int Condition);

public sealed record OrigPhase(int Kind, string Text, int Portrait);

public sealed class OrigJob
{
    public int Number { get; init; }
    public int CarId { get; init; }
    public float Fee { get; init; }
    public (float R, float G, float B) Color { get; init; } = (0.5f, 0.5f, 0.5f);
    /// <summary>Allowed views: Complete, Engine, Body, Running gear.</summary>
    public bool[] Views { get; init; } = [true, true, true, true];
    /// <summary>0 in the tutorial jobs, 1 in the others (meaning unknown).</summary>
    public int Flag1710 { get; init; }
    public List<OrigPhase> Phases { get; } = [];
    public List<OrigStat> Start { get; } = [];
    public List<OrigStat> Complete { get; } = [];

    public const int Request = 0, Hint = 1, Thanks = 2;
}

public sealed class OrigJobPack
{
    public string Path { get; init; } = "";
    public List<OrigImage?> Portraits { get; } = [];
    public List<OrigJob> Jobs { get; } = [];

    public static OrigJobPack Read(string path)
    {
        using var r = TagReader.Open(path, inMemory: true);
        var root = r.Root();
        if (root.Id != 4) throw new InvalidDataException($"{path} is not a job pack.");
        var pack = new OrigJobPack { Path = path };
        foreach (var c in r.Children(root))
        {
            if (c.Id == 104) pack.Portraits.Add(OrigImage.Read(r, c));
            else if (c.Id == 103) pack.Jobs.Add(ReadJob(r, c));
        }
        return pack;
    }

    static OrigJob ReadJob(TagReader r, TagHeader node)
    {
        int number = 0, car = 0, flag = 0;
        float fee = 0;
        var color = (0.5f, 0.5f, 0.5f);
        bool[] views = [true, true, true, true];
        var phases = new List<OrigPhase>();
        var start = new List<OrigStat>();
        var complete = new List<OrigStat>();
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1700: number = r.Int(c); break;
                case 1701: car = r.Int(c); break;
                case 1703: fee = r.Float(c); break;
                case 1704:
                    var v = r.Vec(c);
                    if (!float.IsNaN(v.X)) color = (v.X, v.Y, v.Z);
                    break;
                case 1706:
                    var f = r.Ints(c);
                    if (f.Length >= 4) views = [f[0] != 0, f[1] != 0, f[2] != 0, f[3] != 0];
                    break;
                case 1710: flag = r.Int(c); break;
                case 1707:
                    int kind = 0, portrait = 0;
                    string text = "";
                    foreach (var p in r.Children(c))
                        if (p.Id == 1800) kind = r.Int(p);
                        else if (p.Id == 1801) text = r.String(p);
                        else if (p.Id == 1802) portrait = r.Int(p);
                    phases.Add(new OrigPhase(kind, text.Trim(), portrait));
                    break;
                case 1708 or 1709:
                    int scope = 0, target = 0, cond = 3;
                    foreach (var s in r.Children(c))
                        if (s.Id == 1900) scope = r.Int(s);
                        else if (s.Id == 1901) target = r.Int(s);
                        else if (s.Id == 1902) cond = r.Int(s);
                    (c.Id == 1708 ? start : complete).Add(new OrigStat(scope == 1, target, cond));
                    break;
            }
        var job = new OrigJob { Number = number, CarId = car, Fee = float.IsNaN(fee) ? 0 : fee, Color = color, Views = views, Flag1710 = flag };
        job.Phases.AddRange(phases);
        job.Start.AddRange(start);
        job.Complete.AddRange(complete);
        return job;
    }
}

public sealed class OrigDecal
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public float Price { get; init; }
    /// <summary>0 no recolouring, 1 tinted with the paint colour.</summary>
    public int Tint { get; init; }
    public int Uses { get; init; } = 1;
    public OrigImage? Image { get; init; }
}

public static class OrigDecalPack
{
    public static List<OrigDecal> Read(string path)
    {
        using var r = TagReader.Open(path, inMemory: true);
        var root = r.Root();
        if (root.Id != 2) throw new InvalidDataException($"{path} is not a decal pack.");
        var list = new List<OrigDecal>();
        foreach (var d in r.Children(root))
        {
            if (d.Id != 101) continue;
            int id = 0, tint = 0, uses = 1;
            string name = "";
            float price = 0;
            OrigImage? img = null;
            foreach (var c in r.Children(d))
                switch (c.Id)
                {
                    case 1100: id = r.Int(c); break;
                    case 1101: name = r.String(c).Trim(); break;
                    case 1102: price = r.Float(c); break;
                    case 1103: tint = r.Int(c); break;
                    case 1104: uses = r.Int(c); break;
                    case 1105: img = OrigImage.Read(r, c, blackIsTransparent: true); break;
                }
            list.Add(new OrigDecal { Id = id, Name = name, Price = float.IsNaN(price) ? 0 : price, Tint = tint, Uses = Math.Max(1, uses), Image = img });
        }
        return list;
    }
}
