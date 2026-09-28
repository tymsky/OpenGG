// The original's mechanic saves (Data/Mechanics/*.mek). Layout: docs/formats/tagged-files.md (".mek").
// Used to measure the original game (compare saves before and after an action) and, later, to import saves.

namespace OpenGG.Core.Original;

public sealed record MekItem(int Part, int Car, int Condition, float Extra);

public sealed class MekCarGroup
{
    public int Car { get; init; }
    /// <summary>1301: probably the player's Parts Bin for this car model.</summary>
    public List<MekItem> Bin { get; } = [];
    /// <summary>1302: probably the JunkYard shelf for this car model.</summary>
    public List<MekItem> Shelf { get; } = [];
}

public sealed class MekWorkshopCar
{
    public int Car { get; init; }
    /// <summary>1501: 1 once "Car Complete" has come up for the car (its clock stops there), else 0.</summary>
    public int Field1501 { get; init; }
    /// <summary>1502: time spent on the car in the WorkShop, in milliseconds (the Car Lot's Repair Time).</summary>
    public int RepairTimeMs { get; init; }
    /// <summary>1503: money spent on the car while you own it (the Car Lot's Repair Cost).</summary>
    public float RepairCost { get; init; }
    /// <summary>1504: what you paid for the car (the Car Lot's Orig Cost).</summary>
    public float Cost { get; init; }
    /// <summary>1505: the car's Number in the Car Lot: the mechanic's count of cars bought (1404) when it was bought.</summary>
    public int Number { get; init; }
    public List<MekItem> Parts { get; } = [];
    public OrigImage? Paint { get; init; }
}

/// <summary>1409: decals bought (1600 decal id, 1601 uses left).</summary>
public sealed record MekDecal(int Id, int Uses);

public sealed class OrigMechanic
{
    public string Name { get; init; } = "";
    public float Cash { get; init; }
    /// <summary>1402: skill level (0 Learning, 1 Novice after the tutorial ...).</summary>
    public int Skill { get; init; }
    /// <summary>1406: cars you own (the sign-in screen's CARS column).</summary>
    public int CarsOwned { get; init; }
    /// <summary>1404: cars bought so far (it counts on after they are sold); the next car bought gets it as its Number.</summary>
    public int CarsBought { get; init; }
    /// <summary>1405: total play time in seconds (the sign-in TOTAL TIME).</summary>
    public int PlayTime { get; init; }
    /// <summary>1410: the Number of your car in the WorkShop; absent while it is empty or holds a customer's car.</summary>
    public int? WorkshopNumber { get; init; }
    /// <summary>1412: 1 until the tutorial's jobs (Jobs Mode) are done.</summary>
    public bool JobsMode { get; init; }
    /// <summary>Scalars we have not identified yet, by field id.</summary>
    public SortedDictionary<int, string> Other { get; } = [];
    public List<MekCarGroup> Groups { get; } = [];
    /// <summary>1408: every car you own, in the order they were bought (the one in the WorkShop and those in the Car Lot).</summary>
    public List<MekWorkshopCar> Cars { get; } = [];
    /// <summary>Your car in the WorkShop, if there is one.</summary>
    public MekWorkshopCar? Workshop => WorkshopNumber is { } n ? Cars.Find(c => c.Number == n) : null;
    public List<MekDecal> Decals { get; } = [];
    /// <summary>1411: bit set by job number, probably the jobs done.</summary>
    public List<int> JobsDone { get; } = [];
}

public static class MekFile
{
    public static OrigMechanic Read(string path)
    {
        using var r = TagReader.Open(path, inMemory: true);
        var root = r.Root();
        if (root.Id != 3) throw new InvalidDataException($"{path} is not a mechanic file.");
        var node = r.Child(root, 102) ?? throw new InvalidDataException($"{path} has no mechanic.");
        string name = "";
        float cash = 0;
        int skill = 0, time = 0, owned = 0, bought = 0;
        int? workshop = null;
        bool jobsMode = false;
        var other = new SortedDictionary<int, string>();
        var groups = new List<MekCarGroup>();
        var cars = new List<MekWorkshopCar>();
        var decals = new List<MekDecal>();
        var jobs = new List<int>();
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1401: name = r.String(c); break;
                case 1403: cash = r.Float(c); break;
                case 1402: skill = r.Int(c); break;
                case 1404: bought = r.Int(c); break;
                case 1406: owned = r.Int(c); break;
                case 1405: time = r.Int(c); break;
                case 1410: workshop = r.Int(c); break;
                case 1412: jobsMode = r.Int(c) != 0; break;
                case 1407: groups.Add(ReadGroup(r, c)); break;
                case 1408: cars.Add(ReadWorkshop(r, c)); break;
                case 1409:
                {
                    int id = -1, uses = 0;
                    foreach (var d in r.Children(c))
                        if (d.Id == 1600) id = r.Int(d);
                        else if (d.Id == 1601) uses = r.Int(d);
                    if (id >= 0) decals.Add(new MekDecal(id, uses));
                    break;
                }
                case 1411:
                    var bits = r.Bytes(c);
                    for (int i = 0; i < bits.Length * 8; i++)
                        if ((bits[i / 8] & (1 << (i % 8))) != 0) jobs.Add(i);
                    break;
                default:
                    other[c.Id] = c.Type switch
                    {
                        TagType.Int => r.Int(c).ToString(),
                        TagType.Float => r.Float(c).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                        TagType.String => r.String(c),
                        _ => $"{c.Type} {c.Length} B",
                    };
                    break;
            }
        var m = new OrigMechanic { Name = name, Cash = cash, Skill = skill, CarsOwned = owned, CarsBought = bought, PlayTime = time, WorkshopNumber = workshop, JobsMode = jobsMode };
        foreach (var kv in other) m.Other[kv.Key] = kv.Value;
        m.Groups.AddRange(groups);
        m.Cars.AddRange(cars);
        m.Decals.AddRange(decals);
        m.JobsDone.AddRange(jobs);
        return m;
    }

    static MekItem ReadItem(TagReader r, TagHeader node)
    {
        int part = 0, car = 0, cond = 0;
        float extra = 0;
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1200: part = r.Int(c); break;
                case 1201: car = r.Int(c); break;
                case 1202: cond = r.Int(c); break;
                case 1203: extra = r.Float(c); break;
            }
        return new MekItem(part, car, cond, extra);
    }

    static MekCarGroup ReadGroup(TagReader r, TagHeader node)
    {
        int car = 0;
        var bin = new List<MekItem>();
        var shelf = new List<MekItem>();
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1300: car = r.Int(c); break;
                case 1301: bin.Add(ReadItem(r, c)); break;
                case 1302: shelf.Add(ReadItem(r, c)); break;
            }
        var g = new MekCarGroup { Car = car };
        g.Bin.AddRange(bin);
        g.Shelf.AddRange(shelf);
        return g;
    }

    static MekWorkshopCar ReadWorkshop(TagReader r, TagHeader node)
    {
        int car = 0, f1501 = 0, f1502 = 0, f1505 = 0;
        float cost = 0, repair = 0;
        var parts = new List<MekItem>();
        OrigImage? paint = null;
        foreach (var c in r.Children(node))
            switch (c.Id)
            {
                case 1500: car = r.Int(c); break;
                case 1501: f1501 = r.Int(c); break;
                case 1502: f1502 = r.Int(c); break;
                case 1503: repair = r.Float(c); break;
                case 1504: cost = r.Float(c); break;
                case 1505: f1505 = r.Int(c); break;
                case 1506: parts.Add(ReadItem(r, c)); break;
                case 1507:
                    if (r.Child(c, 402) is { } img) paint = OrigImage.Read(r, img);
                    break;
            }
        var w = new MekWorkshopCar { Car = car, Field1501 = f1501, RepairTimeMs = f1502, Cost = cost, RepairCost = repair, Number = f1505, Paint = paint };
        w.Parts.AddRange(parts);
        return w;
    }
}
