// Builds a ContentPack from the player's own copy of the original game, read at runtime.
// Nothing from the original is stored in this repository.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenGG.Core.Content;

namespace OpenGG.Core.Original;

public sealed partial class OriginalGame
{
    public const string PackId = "original";

    public string Folder { get; }
    public ContentPack Pack { get; private set; } = new();
    /// <summary>Content car id → the car's file and parts.</summary>
    public Dictionary<string, OrigCarInfo> Cars { get; } = [];
    /// <summary>Content car ids by the number in their .car file, in file order (fan cars can share one).</summary>
    public Dictionary<int, List<string>> CarsByNumber { get; } = [];
    /// <summary>Portraits and decal pictures by asset id.</summary>
    public Dictionary<string, OrigImage> Pictures { get; } = [];
    /// <summary>The game's archives (pictures, sounds, scenes, the random jobs' words and faces), read in memory.</summary>
    public OriginalArchives? Archives { get; private init; }
    public List<string> Problems { get; } = [];

    /// <summary>Where the random jobs' words are in the archives; their faces are next to them.</summary>
    const string OwnPhrases = "random/comments.txt";

    OriginalGame(string folder) => Folder = folder;

    public static string CarsDir(string folder) => Path.Combine(folder, "Data", "Cars");

    /// <summary>Does this look like an installed copy of the original game?</summary>
    public static bool IsGameFolder(string folder) =>
        Directory.Exists(CarsDir(folder)) && Directory.EnumerateFiles(CarsDir(folder), "*.car").Any();

    /// <summary>
    /// The game's folder from the one a player picked: that folder itself; the one above it when they picked Data or
    /// Data\Cars; or the only copy of the game one or two folders down (they picked HeadGames or Program Files). Null
    /// when there is none, or more than one to choose from.
    /// </summary>
    public static string? FindGameFolder(string picked)
    {
        try
        {
            var dir = new DirectoryInfo(picked);
            for (var (d, up) = (dir, 0); d is not null && up <= 2; (d, up) = (d.Parent, up + 1))
                if (IsGameFolder(d.FullName)) return d.FullName;
            if (!dir.Exists) return null;
            var found = new List<string>();
            foreach (var sub in SubFolders(dir))
            {
                if (IsGameFolder(sub.FullName)) found.Add(sub.FullName);
                else found.AddRange(SubFolders(sub).Where(s => IsGameFolder(s.FullName)).Select(s => s.FullName));
                if (found.Count > 1) return null;
            }
            return found.Count == 1 ? found[0] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    static IEnumerable<DirectoryInfo> SubFolders(DirectoryInfo d)
    {
        try { return d.GetDirectories(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }

    /// <param name="basePack">Supplies what the original's readable files don't have: tools, rules, paints, names.</param>
    /// <param name="phrases">Words for the random jobs (see <see cref="PhraseFile"/>) in place of the game's own; without
    /// either Free Play's random jobs are our own templates.</param>
    /// <param name="archives">The game's archives if they are already open.</param>
    public static OriginalGame Load(string folder, ContentPack basePack, JobPhraseBook? phrases = null, OriginalArchives? archives = null)
    {
        var game = new OriginalGame(folder) { Archives = archives ?? OriginalArchives.Open(folder) };
        game.Build(basePack, phrases ?? game.ReadOwnPhrases());
        return game;
    }

    /// <summary>
    /// The Body Paint colours as the game has them: its palette picture (Gfx24/paintcolors.tga) is 3 x 9 pixels, one
    /// per colour, row by row. They keep the base pack's ids and names; null (the base pack's colours stand) when the
    /// picture can't be read or holds another number of colours.
    /// </summary>
    List<PaintDef>? OwnPaints(List<PaintDef> basePaints)
    {
        if (Archives?.Read("Gfx24/paintcolors.tga") is not { } bytes) return null;
        OrigImage img;
        try
        {
            img = TgaFile.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            Problems.Add($"Gfx24/paintcolors.tga: {e.Message}");
            return null;
        }
        if (img.Width * img.Height != basePaints.Count)
        {
            Problems.Add($"Gfx24/paintcolors.tga: {img.Width * img.Height} colours, not {basePaints.Count}");
            return null;
        }
        return basePaints.Select((p, i) => new PaintDef
        {
            Id = p.Id,
            Name = p.Name,
            Color = $"#{img.Rgba[i * 4]:x2}{img.Rgba[i * 4 + 1]:x2}{img.Rgba[i * 4 + 2]:x2}",
        }).ToList();
    }

    /// <summary>The random jobs' own words, from the game's archive; a face is "orig-face:random/&lt;file&gt;".</summary>
    JobPhraseBook? ReadOwnPhrases()
    {
        if (Archives?.Read(OwnPhrases) is not { } bytes) return null;
        var dir = OwnPhrases[..(OwnPhrases.LastIndexOf('/') + 1)];
        var skipped = new List<string>();
        var book = PhraseFile.Parse(Encoding.Latin1.GetString(bytes), face => $"orig-face:{dir}{face}", skipped);
        foreach (var s in skipped) Problems.Add($"{OwnPhrases}: skipped {s}");
        return book;
    }

    // ---- building ---------------------------------------------------------------------------------

    void Build(ContentPack basePack, JobPhraseBook? phrases)
    {
        var pack = new ContentPack
        {
            Id = PackId,
            Name = "Your copy of Gearhead Garage",
            Tools = basePack.Tools,
            FastenerKinds = basePack.FastenerKinds,
            Names = basePack.Names,
            Paints = OwnPaints(basePack.Paints) ?? basePack.Paints,
            Rules = Json.Parse<RulesDef>(Json.Write(basePack.Rules)),
        };
        // Measured in the 2002 release: a new mechanic starts with $5000.
        pack.Rules.StartCash = 5000;
        // Measured: the levels go by cash alone. A mechanic with the tutorial's 9 jobs and one car bought, the save's cash
        // set to $50,000, went up to Handy and on to Expert as it signed in.
        foreach (var level in pack.Rules.Skills) level.Cars = 0;
        var byCarId = new Dictionary<int, List<(string Id, OrigCarInfo Info)>>();
        var usedIds = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(CarsDir(Folder), "*.car").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            OrigCarInfo info;
            try
            {
                info = CarFile.ReadInfo(file);
            }
            catch (Exception e)
            {
                Problems.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }
            if (info.Parts.Count == 0) continue;
            string id = "orig." + Slug(Path.GetFileNameWithoutExtension(file));
            for (int n = 2; !usedIds.Add(id); n++) id = $"orig.{Slug(Path.GetFileNameWithoutExtension(file))}_{n}";
            Cars[id] = info;
            if (!byCarId.TryGetValue(info.CarId, out var list)) byCarId[info.CarId] = list = [];
            list.Add((id, info));
            if (!CarsByNumber.TryGetValue(info.CarId, out var ids)) CarsByNumber[info.CarId] = ids = [];
            ids.Add(id);
            var (car, parts) = BuildCar(id, info, pack.Paints);
            pack.Cars.Add(car);
            pack.Parts.AddRange(parts);
        }

        AddJobs(pack, byCarId);
        AddDecals(pack);
        if (phrases is { Customers.Count: > 0 }) AddPhrasedJobs(pack, phrases);
        else AddRandomJobs(pack, basePack);
        AdaptDiagnosis(pack);
        pack.Normalize();
        Pack = pack;
    }

    static readonly string[] RegionKeys = ["all", "engine", "body", "running_gear"];

    static Region RegionOf(int r) => r switch { 1 => Region.Engine, 3 => Region.RunningGear, _ => Region.Body };

    (CarModelDef, List<PartDef>) BuildCar(string carId, OrigCarInfo info, List<PaintDef> paints)
    {
        var (year, make, model) = SplitName(info.Name);
        var parts = info.Parts;

        // Parts that are alternatives of each other (sharing a mutual-exclusion bit) share a slot.
        var groupOf = Enumerable.Range(0, parts.Count).ToArray();
        int Find(int i) => groupOf[i] == i ? i : groupOf[i] = Find(groupOf[i]);
        for (int bit = 0; bit < 32; bit++)
        {
            int first = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                if ((parts[i].MutualExc & (1 << bit)) == 0) continue;
                if (first < 0) first = i;
                else groupOf[Find(i)] = Find(first);
            }
        }
        var groups = Enumerable.Range(0, parts.Count).GroupBy(Find).Select(g => g.Select(i => parts[i]).ToList()).ToList();

        var slotOfPart = new Dictionary<int, string>();
        var groupSlot = new List<(List<OrigPart> Members, string SlotId)>();
        foreach (var g in groups)
        {
            var rep = g.Where(p => !p.Custom).OrderBy(p => p.Number).FirstOrDefault() ?? g.OrderBy(p => p.Number).First();
            string slotId = $"p{rep.Number}";
            foreach (var p in g) slotOfPart[p.Number] = slotId;
            groupSlot.Add((g, slotId));
        }
        var withAlternatives = groupSlot.Where(g => g.Members.Count > 1).Select(g => g.SlotId).ToHashSet();

        var meshNames = info.Meshes.Select(m => m.Name).ToHashSet();
        // The stock car: the parts the file does not mark custom, but only those that go on stock parts. Measured: a part
        // left unmarked that goes on a custom one belongs to that add-on, and job cars came without it (a T-Bird's
        // HotRod Dual Carbs and Blower on the custom HotRod manifold, a Fairmont's Boosters on the custom Booster Support,
        // which a job had to buy and fit).
        var stockParts = parts.Where(p => !p.Custom).Select(p => p.Number).ToHashSet();
        var numbers = parts.Select(p => p.Number).ToHashSet();
        for (bool dropped = true; dropped;)
        {
            dropped = false;
            foreach (var p in parts)
            {
                if (!stockParts.Contains(p.Number)) continue;
                var deps = p.AttachDep.Where(numbers.Contains).ToList();
                if (deps.Count == 0 || (p.Amea ? deps.Any(stockParts.Contains) : deps.All(stockParts.Contains))) continue;
                stockParts.Remove(p.Number);
                dropped = true;
            }
        }
        var stockSlots = groupSlot.Where(g => g.Members.Any(p => stockParts.Contains(p.Number))).Select(g => g.SlotId).ToHashSet();
        var partDefs = new List<PartDef>();
        var slots = new List<SlotDef>();
        foreach (var (members, slotId) in groupSlot)
        {
            var stock = members.Where(p => stockParts.Contains(p.Number)).OrderBy(p => p.Number).FirstOrDefault();
            var rep = stock ?? members.OrderBy(p => p.Number).First();
            string slotType = $"{carId}/{slotId}";
            foreach (var p in members)
            {
                partDefs.Add(new PartDef
                {
                    Id = PartId(carId, p.Number),
                    Name = p.Name,
                    Category = RegionOf(p.Region).Label(),
                    SlotType = slotType,
                    Model = meshNames.Contains(p.Mesh) ? $"orig:{carId}/{p.Mesh}" : "",
                    Price = Money(p.CostMax),
                    PriceMin = Money(p.CostMin),
                    Custom = p.Custom,
                    ValueBonus = p.Custom ? Money(p.CostMax / 2) : 0,
                    // Bolts are in car space; original slots sit at the car origin, so that is part space too.
                    Fasteners = p.Bolts.Select(b => new FastenerDef { Kind = "bolt", Pos = b, Dir = Vec3.Zero }).ToList(),
                    Mounts = Map(p.AttachDep, slotId, stockParts.Contains(p.Number) ? stockSlots : null),
                    MountsAny = p.Amea,
                    NeedsParts = p.Amea ? null : Exact(p.AttachDep, slotId),
                    RemoveAfter = Map(p.RemoveDep, slotId, null),
                    Sound = info.SoundParts.Contains(p.Number) ? $"orig-sound:{carId}/{p.Number}" : null,
                    SoundKind = p.Special switch { 2 => PartSound.Start, 4 => PartSound.Accessory, _ => PartSound.Run },
                    Spins = p.Special == 3,
                    Block = p.Special == 1,
                    AddOn = !p.Custom && !stockParts.Contains(p.Number),
                });
            }
            var mesh = info.Meshes.Find(m => m.Name == rep.Mesh);
            var center = mesh?.Pivot ?? Vec3.Zero;
            var outward = new Vec3(center.X * 0.3, 0, center.Z).Normalized();
            slots.Add(new SlotDef
            {
                Id = slotId,
                Name = rep.Name,
                SlotType = slotType,
                Family = Family(rep.Name),
                Region = RegionOf(rep.Region),
                // Mounting of the default part; other parts add their own through PartDef.Mounts.
                // AMEA parts may hang on any one of their parents, so they don't pin a single one.
                Parents = rep.Amea ? [] : Map(rep.AttachDep, slotId, stock is null ? null : stockSlots) ?? [],
                RemoveDir = (outward + new Vec3(0, 0.6, 0)).Normalized(),
                Required = stock is not null && stock.Special != 4,
                DefaultPart = stock is null ? null : PartId(carId, stock.Number),
            });
        }

        // Part numbers → slots, without the part's own slot (alternatives listed against each other).
        // A stock part can only depend on slots the stock car fills, or the stock car could not exist.
        List<string>? Map(int[] numbers, string self, HashSet<string>? only)
        {
            var list = numbers.Where(slotOfPart.ContainsKey).Select(n => slotOfPart[n])
                .Where(s => s != self && (only is null || only.Contains(s))).Distinct().ToList();
            return list.Count > 0 ? list : null;
        }

        // Without AMEA a part goes on the very parts it names, not on an alternative in their place (measured: the
        // Escort's Cosworth job was done with the Trunk and Back Windshield in the Parts Bin, its Convertible job ASSEMBLED
        // with the Trunk and both windshields there). Only parents that have alternatives need naming.
        List<string>? Exact(int[] numbers, string self)
        {
            var list = numbers.Where(n => slotOfPart.TryGetValue(n, out var s) && s != self && withAlternatives.Contains(s))
                .Select(n => PartId(carId, n)).Distinct().ToList();
            return list.Count > 0 ? list : null;
        }

        BreakRemovalCycles(slots, partDefs, carId, info.Name);
        // The Catalog lists a car's parts in the order of its file (measured), so keep that order.
        var fileOrder = parts.Select((p, i) => (PartId(carId, p.Number), i)).GroupBy(t => t.Item1).ToDictionary(g => g.Key, g => g.First().i);
        partDefs = partDefs.OrderBy(p => fileOrder.GetValueOrDefault(p.Id, int.MaxValue)).ToList();

        var used = parts.Select(p => p.Mesh).ToHashSet();
        var statics = info.Meshes
            .Where(m => m.Name.Length > 1 && "#$^".Contains(m.Name[0]) && !used.Contains(m.Name))
            .Select(m => m.Name[0] switch { '#' => Region.Engine, '^' => Region.RunningGear, _ => Region.Body })
            .Distinct()
            .Select(r => new StaticModelDef { Model = $"orig:{carId}/@static/{RegionKeys[(int)r + 1]}", Region = r })
            .ToList();

        var car = new CarModelDef
        {
            Id = carId,
            Make = make,
            Name = model,
            Year = year,
            BodyStyle = TruckWords().IsMatch(info.Name) ? "pickup" : "car",
            EngineLabel = EngineLabel(parts),
            BodyModel = "",
            PaintMaterial = "paint",
            PaintAtlas = true,
            DefaultPaint = paints.Count > 0 ? paints[(int)(Hash(carId) % (uint)paints.Count)].Color : "#808080",
            OwnPaint = info.OwnPaint ?? "",
            BaseValue = Money(parts.Where(p => !p.Custom).Sum(p => p.CostMax)),
            MinSkill = Math.Clamp(info.MinSkill, 0, 4),
            Slots = slots,
            Statics = statics,
        };
        return (car, partDefs);
    }

    public static string PartId(string carId, int number) => $"{carId}.{number}";

    /// <summary>
    /// Some cars (fan-made ones, mostly) have parts that each must come off before the other, so the
    /// stock car could never be taken apart. Drop the "remove first" links that close such loops.
    /// </summary>
    void BreakRemovalCycles(List<SlotDef> slots, List<PartDef> partDefs, string carId, string carName)
    {
        var defaults = slots.Where(s => s.DefaultPart is not null).ToDictionary(s => s.Id, s => partDefs.First(p => p.Id == s.DefaultPart));
        // Edges: slot → slots that must come off before it: parts mounted on it (kind 0: slot parents,
        // 1: the part's own mounts) and its "remove first" list (kind 2, dropped first).
        IEnumerable<(string To, int Kind)> Edges(string id)
        {
            if (defaults.TryGetValue(id, out var p) && p.RemoveAfter is { } after)
                foreach (var t in after.Where(defaults.ContainsKey)) yield return (t, 2);
            foreach (var c in slots.Where(c => c.DefaultPart is not null))
            {
                if (c.Parents.Contains(id)) yield return (c.Id, 0);
                else if (defaults[c.Id].Mounts?.Contains(id) ?? false) yield return (c.Id, 1);
            }
        }
        var state = new Dictionary<string, int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            state.Clear();
            foreach (var s in defaults.Keys)
                if (Visit(s)) { changed = true; break; }
        }

        bool Visit(string id)
        {
            if (state.GetValueOrDefault(id) == 2) return false;
            state[id] = 1;
            foreach (var (to, kind) in Edges(id).ToList())
            {
                if (state.GetValueOrDefault(to) == 1)
                {
                    if (kind == 2) defaults[id].RemoveAfter!.Remove(to);
                    else if (kind == 0) slots.First(x => x.Id == to).Parents.Remove(id);
                    else defaults[to].Mounts!.Remove(id);
                    Problems.Add($"{carName}: dropped a loop between {id} and {to}");
                    return true;
                }
                if (Visit(to)) return true;
            }
            state[id] = 2;
            return false;
        }
    }

    /// <summary>Keeps the stored value (fees like 77.36667 are shown as $77.37 but paid in full).</summary>
    static decimal Money(float v) => (decimal)Math.Max(0, v);

    static string EngineLabel(List<OrigPart> parts)
    {
        var block = parts.Find(p => p.Special == 1 && !p.Custom) ?? parts.Find(p => p.Name.Contains("block", StringComparison.OrdinalIgnoreCase));
        if (block is null) return "";
        var label = Regex.Replace(block.Name, @"\bblock\b", "", RegexOptions.IgnoreCase).Trim();
        return label.Length > 0 ? label : block.Name;
    }

    /// <summary>"1977 Ford Pickup" → (1977, "Ford", "Pickup"); "69 Camaro" → (1969, "", "Camaro").</summary>
    static (int Year, string Make, string Model) SplitName(string name)
    {
        var m = Regex.Match(name, @"^\s*'?(\d{4}|\d{2})\s+(.+)$");
        if (!m.Success) return (0, "", name);
        int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (y < 100) y += y < 30 ? 2000 : 1900;
        if (y < 1880 || y > 2030) return (0, "", name);
        var rest = m.Groups[2].Value.Trim();
        var sp = rest.IndexOf(' ');
        return sp > 0 ? (y, rest[..sp], rest[(sp + 1)..]) : (y, "", rest);
    }

    [GeneratedRegex(@"pick ?up|truck|\bute\b|f-?1[05]0|f-?250|f-?350|ranger|s-?10|\bram\b|sierra|tacoma|dually|bronco|blazer|jeep|hummer|\bgmc\b", RegexOptions.IgnoreCase)]
    private static partial Regex TruckWords();

    static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    public static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    }

    static readonly (string Word, string Family)[] FamilyWords =
    [
        ("air filter", "air_filter"), ("air cleaner", "air_filter"), ("alternator", "alternator"), ("battery", "battery"),
        ("starter", "starter"), ("distributor", "distributor"), ("carb", "carburetor"), ("intake", "intake_manifold"),
        ("block", "engine_block"), ("crank", "crankshaft"), ("flywheel", "flywheel"), ("oil pan", "oil_pan"),
        ("fan", "radiator_fan"), ("radiator", "radiator"), ("valve cover", "valve_cover"), ("head", "cylinder_head"),
        ("spark", "spark_plug"), ("plug", "spark_plug"), ("coil", "ignition_coil"), ("water pump", "water_pump"),
        ("wheel", "wheel"), ("tire", "wheel"), ("shock", "shock"), ("strut", "strut"), ("muffler", "muffler"),
        ("exhaust", "muffler"), ("transmission", "transmission"), ("drive", "driveshaft"), ("axle", "axle"),
        ("hood", "hood"), ("fender", "fender"), ("door", "door"), ("windshield", "windshield"), ("window", "window"),
        ("roof", "roof"), ("trunk", "trunk"), ("bumper", "bumper"), ("spoiler", "spoiler"), ("grill", "grille"),
    ];

    /// <summary>A functional family shared across cars (used by random jobs and Start Engine).</summary>
    public static string Family(string partName)
    {
        var n = partName.ToLowerInvariant();
        foreach (var (w, f) in FamilyWords)
            if (n.Contains(w)) return f;
        var words = Slug(n).Split('_').Where(w => w is not ("left" or "right" or "front" or "back" or "rear" or "l" or "r")).ToArray();
        return words.Length > 0 ? string.Join('_', words) : "part";
    }

    // ---- jobs ----------------------------------------------------------------------------------------

    void AddJobs(ContentPack pack, Dictionary<int, List<(string Id, OrigCarInfo Info)>> byCarId)
    {
        var jobsDir = Path.Combine(Folder, "Data", "Jobs");
        if (!Directory.Exists(jobsDir)) return;
        foreach (var file in Directory.EnumerateFiles(jobsDir, "*.jpk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            OrigJobPack jp;
            try
            {
                jp = OrigJobPack.Read(file);
            }
            catch (Exception e)
            {
                Problems.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }
            string stem = Slug(Path.GetFileNameWithoutExtension(file));
            for (int i = 0; i < jp.Portraits.Count; i++)
                if (jp.Portraits[i] is { } img)
                {
                    var pid = $"orig-portrait:{stem}/{i}";
                    Pictures[pid] = img;
                    pack.Portraits.Add(pid);
                }
            foreach (var j in jp.Jobs)
            {
                if (!byCarId.TryGetValue(j.CarId, out var candidates))
                {
                    Problems.Add($"{Path.GetFileName(file)} job {j.Number}: no car with id {j.CarId}");
                    continue;
                }
                // Several files can share a car id (fan cars); prefer the one named like the job pack.
                var (carId, info) = candidates.FirstOrDefault(c => c.Id == $"orig.{stem}") is { Id: not null } named ? named : candidates[0];
                string Portrait(int index) => index >= 0 && index < jp.Portraits.Count && jp.Portraits[index] is not null ? $"orig-portrait:{stem}/{index}" : "";
                var request = j.Phases.Find(p => p.Kind == OrigJob.Request);
                var thanks = j.Phases.Find(p => p.Kind == OrigJob.Thanks);
                var tabs = new List<View>();
                if (j.Views[0]) tabs.Add(View.Complete);
                if (j.Views[1]) tabs.Add(View.Engine);
                if (j.Views[2]) tabs.Add(View.Body);
                if (j.Views[3]) tabs.Add(View.RunningGear);
                // Jobs marked 0 (the tutorial pack) are the Jobs Mode chain; the others are offered in Free Play.
                bool chain = j.Flag1710 == 0;
                pack.Jobs.Add(new JobTemplateDef
                {
                    Id = $"jpk.{stem}.{j.Number}",
                    Sequence = chain ? j.Number : null,
                    CarId = carId,
                    Text = request?.Text ?? "",
                    Hints = j.Phases.Where(p => p.Kind == OrigJob.Hint).Select(p => p.Text).ToList(),
                    Thanks = thanks?.Text,
                    Portrait = Portrait(request?.Portrait ?? -1),
                    ThanksPortrait = Portrait(thanks?.Portrait ?? -1),
                    Fee = Money(j.Fee),
                    Paint = ToHex(j.Color),
                    Tabs = tabs,
                    Difficulty = Sim.Economy.DifficultyOf(pack.Rules, Money(j.Fee)),
                    Start = j.Start.Select(s => Stat(s, carId, info)).Where(s => s is not null).Select(s => s!).ToList(),
                    Complete = j.Complete.Select(s => Stat(s, carId, info)).Where(s => s is not null).Select(s => s!).ToList(),
                    Weight = chain ? 0 : 1,
                });
            }
        }
    }

    JobStatDef? Stat(OrigStat s, string carId, OrigCarInfo info)
    {
        int cond = Math.Clamp(s.Condition, -1, 3);
        if (s.Region) return new JobStatDef { Region = RegionKeys[Math.Clamp(s.Target, 0, 3)], Condition = cond };
        if (!info.Parts.Exists(p => p.Number == s.Target)) return null;
        return new JobStatDef { Part = PartId(carId, s.Target), Condition = cond };
    }

    static string ToHex((float R, float G, float B) c) =>
        $"#{(int)Math.Clamp(c.R * 255, 0, 255):x2}{(int)Math.Clamp(c.G * 255, 0, 255):x2}{(int)Math.Clamp(c.B * 255, 0, 255):x2}";

    /// <summary>
    /// Free Play's random jobs written with the original's own words. How often each difficulty comes up is
    /// ours (the car packs' jobs, each done once, keep coming up about as often at first).
    /// </summary>
    static void AddPhrasedJobs(ContentPack pack, JobPhraseBook phrases)
    {
        pack.Phrases = phrases;
        (Difficulty Difficulty, double Weight)[] kinds = [(Difficulty.Easy, 3), (Difficulty.Medium, 3), (Difficulty.Hard, 2), (Difficulty.Expert, 2)];
        foreach (var (d, w) in kinds)
            pack.Jobs.Add(new JobTemplateDef { Id = $"phrased.{d.ToString().ToLowerInvariant()}", Difficulty = d, Weight = w, Phrased = true });
    }

    /// <summary>Free Play jobs: our random templates, kept where the original cars have the families they need.</summary>
    static void AddRandomJobs(ContentPack pack, ContentPack basePack)
    {
        var families = pack.Cars.SelectMany(c => c.Slots.Where(s => s.DefaultPart is not null).Select(s => s.Family)).ToHashSet();
        foreach (var t in basePack.Jobs)
        {
            if (t.Sequence is not null) continue;
            if (t.Requirements.Count == 0 || t.Requirements.Any(r => r.Type == JobReqType.Install || !r.Families.Any(families.Contains))) continue;
            var copy = Json.Parse<JobTemplateDef>(Json.Write(t));
            foreach (var r in copy.Requirements) r.Families = r.Families.Where(families.Contains).ToList();
            copy.BodyStyles = null;
            pack.Jobs.Add(copy);
        }
    }

    /// <summary>Start Engine for the original cars, as measured: without its starter, or with any part of the engine
    /// but its block red or black, the starter clicks, whatever is missing too (a red pulley and two red belts did; a job
    /// car's red parts did with its alternator, its crankshaft or its starter off; a yellow Distributor still started);
    /// else any other part the engine needs missing, or its block red or black, it cranks once without catching (the
    /// Alternator, the Air Filter, the Carb, a valve cover, a brace, the power system, one at a time; an F350 with only its
    /// red block and a new starter; the same F350 with its whole engine new but the red block, and then without its
    /// Flywheel or its Oilpan); nothing else stops it or changes its sound.</summary>
    static void AdaptDiagnosis(ContentPack pack)
    {
        var engine = pack.Cars.SelectMany(c => c.Slots.Where(s => s.Required && s.Region == Region.Engine)).Select(s => s.Family).ToHashSet();
        var d = pack.Rules.Diagnosis;
        d.Dead = [];
        d.NoCrank = engine.Where(f => f == "starter").ToList();
        d.NoStart = engine.Except(d.NoCrank).ToList();
        d.Rough = [];
        d.Loud = [];
        d.WorkingCondition = Condition.Red;
        d.EngineClicksBelow = Condition.Yellow;
        d.BlockCranksBelow = Condition.Yellow;
    }

    // ---- decals ----------------------------------------------------------------------------------------

    void AddDecals(ContentPack pack)
    {
        var dir = Path.Combine(Folder, "Data", "Decals");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.dpk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            List<OrigDecal> decals;
            try
            {
                decals = OrigDecalPack.Read(file);
            }
            catch (Exception e)
            {
                Problems.Add($"{Path.GetFileName(file)}: {e.Message}");
                continue;
            }
            string stem = Slug(Path.GetFileNameWithoutExtension(file));
            foreach (var d in decals)
            {
                if (d.Image is null) continue;
                var tex = $"orig-decal:{stem}/{d.Id}";
                Pictures[tex] = d.Image;
                pack.Decals.Add(new DecalDef
                {
                    Id = $"dpk.{stem}.{d.Id}",
                    Name = d.Name,
                    Texture = tex,
                    Price = Money(d.Price),
                    Aspect = d.Image.Width / (double)Math.Max(1, d.Image.Height),
                    Uses = d.Uses,
                    Tint = d.Tint != 0,
                });
            }
        }
    }
}
