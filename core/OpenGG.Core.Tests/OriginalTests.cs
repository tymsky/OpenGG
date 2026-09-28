using OpenGG.Core.Content;
using OpenGG.Core.Original;
using OpenGG.Core.Sim;
using Xunit.Abstractions;
using static OpenGG.Core.Tests.TestUtil;

namespace OpenGG.Core.Tests;

/// <summary>Runs only when OPENGG_ORIGINAL points at an installed copy of the original game.</summary>
public sealed class OriginalFactAttribute : FactAttribute
{
    public OriginalFactAttribute()
    {
        if (OriginalTests.Folder is null) Skip = "Set OPENGG_ORIGINAL to your Gearhead Garage folder to run this test.";
    }
}

/// <summary>Runs only when OPENGG_ORIGINAL points at a copy of the original game with its random jobs' archive.</summary>
public sealed class PhrasesFactAttribute : FactAttribute
{
    public PhrasesFactAttribute()
    {
        if (OriginalTests.Folder is null || OriginalTests.Phrases is null)
            Skip = "Set OPENGG_ORIGINAL to a Gearhead Garage folder that has Data/Jobs/random.dat to run this test.";
    }
}

public class OriginalTests(ITestOutputHelper output)
{
    /// <summary>The random jobs' phrase file, read in memory from the original's archive.</summary>
    public static string? Phrases =>
        Folder is { } f && OriginalArchives.Open(f)?.Read("random/comments.txt") is { } bytes ? System.Text.Encoding.Latin1.GetString(bytes) : null;

    public static string? Folder
    {
        get
        {
            var f = Environment.GetEnvironmentVariable("OPENGG_ORIGINAL");
            return f is not null && OriginalGame.IsGameFolder(f) ? f : null;
        }
    }

    static OriginalGame? game;
    static OriginalGame Game => game ??= OriginalGame.Load(Folder!, Ai.Pack);

    [OriginalFact]
    public void BuildsAConsistentPack()
    {
        var g = Game;
        foreach (var p in g.Problems) output.WriteLine("problem: " + p);
        var errors = Validation.Validate(g.Pack);
        foreach (var e in errors.Take(40)) output.WriteLine("invalid: " + e);
        Assert.Empty(errors);
        Assert.True(g.Pack.Cars.Count > 100, $"cars: {g.Pack.Cars.Count}");
        // The tutorial pack is the Jobs Mode chain; the car packs' jobs are offered in Free Play.
        Assert.Equal(9, g.Pack.Jobs.Count(j => j.Sequence is not null));
        Assert.Equal(22, g.Pack.Jobs.Count(j => j.Sequence is null && j.CarId is not null));
        Assert.Equal(5000m, g.Pack.Rules.StartCash);
        Assert.NotEmpty(g.Pack.Decals);
        output.WriteLine($"{g.Pack.Cars.Count} cars, {g.Pack.Parts.Count} parts, {g.Pack.Jobs.Count} jobs, {g.Pack.Decals.Count} decals, {g.Pack.Portraits.Count} portraits");
    }

    [OriginalFact]
    public void EveryCarComesApartAndGoesBackTogether()
    {
        var ci = new ContentIndex(Game.Pack);
        var failed = new List<string>();
        foreach (var car in Game.Pack.Cars)
        {
            var g = Sim.Game.Create(ci, "Test", 5);
            var v = g.NewVehicle(car, Owner.Player, AllGood);
            g.State.Lot.Add(v.Id);
            g.BringToWorkshop(v.Id);
            var uidBySlot = car.Slots.Where(s => v.Slots[s.Id].Part is not null).ToDictionary(s => s.Id, s => v.Slots[s.Id].Part!.Uid);
            var order = DisassembleAll(g);
            var stuck = car.Slots.Where(s => v.Slots[s.Id].Part is not null).Select(s => s.Id).ToList();
            if (stuck.Count > 0)
            {
                failed.Add($"{car.Id}: can't remove {string.Join(", ", stuck.Take(5).Select(id => $"{id} ({VehicleRules.RemoveCheck(ci, v, id).Msg})"))}");
                continue;
            }
            try
            {
                Reassemble(g, order, id => uidBySlot[id]);
                if (!VehicleRules.GetCompleteness(ci, v).Done) failed.Add($"{car.Id}: not complete after reassembly");
            }
            catch (Exception e)
            {
                failed.Add($"{car.Id}: {e.Message}");
            }
        }
        foreach (var f in failed) output.WriteLine(f);
        Assert.True(failed.Count == 0, $"{failed.Count} of {Game.Pack.Cars.Count} cars failed; first: {failed.FirstOrDefault()}");
    }

    [OriginalFact]
    public void JobsModeStartsWithTheTutorial()
    {
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 5);
        Assert.True(g.InJobsMode);
        var offer = g.RequestJob().Data!;
        var first = g.JobChain.First();
        Assert.Equal(first.Id, offer.TemplateId);
        Assert.Equal(first.Fee, offer.Fee);
        Assert.NotNull(offer.Tabs);
        Assert.True(g.AcceptJob().Ok);
        Assert.False(g.JobProgress().Done);
        output.WriteLine($"{offer.Text}\nfee {offer.Fee}, tabs {string.Join(",", offer.Tabs!)}");
        foreach (var line in g.JobProgress().Open) output.WriteLine("todo: " + line);
    }

    [OriginalFact]
    public void TheFirstJobCanBeFinished()
    {
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 5);
        g.RequestJob();
        g.AcceptJob();
        var v = g.WorkshopVehicle()!;
        var car = ci.Car(v.ModelId);
        // Take everything that is not green off, repair it (or buy a new one), put it back.
        var order = DisassembleAll(g);
        var uid = new Dictionary<string, string>();
        foreach (var item in g.State.Bin.ToList())
        {
            var slot = car.Slots.First(s => s.SlotType == ci.Part(item.Part.PartId).SlotType);
            uid[slot.Id] = item.Part.Uid;
            if (item.Part.Condition == Condition.Black) uid[slot.Id] = g.BuyPart(item.Part.PartId).Data!;
            else if (item.Part.Condition < Condition.Green) Assert.True(g.RepairItem(item.Part.Uid).Ok, "repair");
        }
        foreach (var s in car.Slots.Where(s => s.Required && !uid.ContainsKey(s.Id)))
        {
            uid[s.Id] = g.BuyPart(s.DefaultPart!).Data!;
            order.Add(s.Id);
        }
        Reassemble(g, order.Distinct().ToList(), id => uid[id]);
        Assert.Null(g.State.Job);
        Assert.Contains(g.JobChain.First().Id, g.State.CompletedJobs);
        output.WriteLine($"cash after the first job: {g.State.Cash}");
    }

    [OriginalFact]
    public void TutorialJobsReadEasyThenMedium()
    {
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 5);
        var words = g.JobChain.Select(t => g.MakeScriptedJob(t).Difficulty).ToList();
        // Measured: jobs 1-6 ($100-$225) Easy, 7-9 ($250-$300) Medium.
        Assert.Equal([.. Enumerable.Repeat(Difficulty.Easy, 6), .. Enumerable.Repeat(Difficulty.Medium, 3)], words);
    }

    [OriginalFact]
    public void FreePlayOffersTheCarPacksJobsAfterTheTutorial()
    {
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 5);
        g.State.CompletedJobs.AddRange(g.JobChain.Select(j => j.Id));
        g.State.FreePlay = true;
        g.State.Skill = 1;
        var seen = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            var offer = g.RequestJob().Data!;
            seen.Add(offer.TemplateId);
            g.State.Offer = null;
            g.State.Vehicles.Remove(offer.VehicleId);
        }
        output.WriteLine(string.Join(", ", seen.Order()));
        Assert.Contains(seen, id => id.StartsWith("jpk.escort."));
        Assert.DoesNotContain(seen, id => id.StartsWith("jpk.tutorial."));
    }

    [OriginalFact]
    public void CarsBringTheirOwnSounds()
    {
        var withSound = Game.Pack.Parts.Where(p => p.Sound is not null).ToList();
        output.WriteLine($"{withSound.Count} parts with sounds, in {Game.Cars.Values.Count(c => c.SoundParts.Count > 0)} cars");
        Assert.True(withSound.Count >= 59);
        Assert.Contains(withSound, p => p.SoundKind == PartSound.Start);
        Assert.Contains(withSound, p => p.SoundKind == PartSound.Accessory);
        var info = Game.Cars.Values.First(c => c.SoundParts.Count > 1);
        var data = CarFile.ReadFull(info.Path);
        Assert.All(info.SoundParts, n => Assert.True(data.Sounds.ContainsKey(n)));
    }

    [OriginalFact]
    public void ImportsTheOriginalsMechanics()
    {
        var ci = new ContentIndex(Game.Pack);
        foreach (var file in Directory.GetFiles(Path.Combine(Folder!, "Data", "Mechanics"), "*.mek"))
        {
            var m = MekFile.Read(file);
            var (g, problems) = MekImport.Import(Game, ci, m, 5);
            var s = g.State;
            output.WriteLine($"{Path.GetFileName(file)} {m.Name}: cash {s.Cash}, skill {s.Skill}, jobs {s.CompletedJobs.Count}, cars {s.Vehicles.Count}, bin {s.Bin.Count}, shelves {s.Shelves.Count} ({s.Shelves.Values.Sum(x => x.Count)} parts), decals {s.DecalUses.Count}");
            foreach (var p in problems) output.WriteLine("  problem: " + p);
            foreach (var v in s.Vehicles.Values)
                output.WriteLine($"  {v.ModelId}: {v.Slots.Values.Count(x => x.Part is not null)} parts, cost {v.Stats.OrigCost}, repair {v.Stats.RepairCost}, {v.Stats.RepairTime} s, paint {v.PaintImage?.Length ?? 0} chars, workshop {s.Workshop == v.Id}");
            Assert.Equal(m.Name.Trim(), s.Mechanic);
            Assert.Equal(m.Cars.Count, s.Vehicles.Count);
            Assert.Equal(m.JobsDone.Count(n => n != MekImport.RandomJobNumber), s.CompletedJobs.Count);
            Assert.Equal(m.Cars.Sum(c => c.Parts.Count), s.Vehicles.Values.Sum(v => v.Slots.Values.Count(x => x.Part is not null)));
            Assert.Equal(m.Groups.Sum(x => x.Bin.Count), s.Bin.Count);
            Assert.Equal(m.Groups.Sum(x => x.Shelf.Count), s.Shelves.Values.Sum(x => x.Count));
            Assert.Empty(problems);
            var again = Sim.Game.Load(ci, g.Save());
            Assert.Equal(s.Cash, again.State.Cash);
        }
    }

    [OriginalFact]
    public void ImportsTheCarLotsNumbersAndYourCarInTheWorkShop()
    {
        // Read from the saves of a session that bought twelve cars and sold them again: a car's Number is its 1505,
        // the mechanic's 1404 counts the cars bought and 1410 names the car in the WorkShop by its Number.
        var ci = new ContentIndex(Game.Pack);
        var ids = Game.CarsByNumber.Where(kv => kv.Value.Exists(ci.HasCar)).Select(kv => kv.Key).Take(3).ToList();
        var m = new OrigMechanic { Name = "Lot", Cash = 1000, Skill = 3, CarsOwned = 3, CarsBought = 16, WorkshopNumber = 9 };
        m.Cars.Add(new MekWorkshopCar { Car = ids[0], Number = 4 });
        m.Cars.Add(new MekWorkshopCar { Car = ids[1], Number = 9 });
        m.Cars.Add(new MekWorkshopCar { Car = ids[2], Number = 15 });
        var g = MekImport.Import(Game, ci, m, 5).Game;
        Assert.Equal(9, g.WorkshopVehicle()!.Number);
        Assert.Equal([4, 15], g.State.Lot.Select(id => g.Vehicle(id).Number!.Value));
        Assert.Equal(16, g.State.Stats.CarsBought);
        m = new OrigMechanic { Name = "Lot", Cash = 1000, CarsOwned = 1, CarsBought = 1 };
        m.Cars.Add(new MekWorkshopCar { Car = ids[0], Number = 0 });
        g = MekImport.Import(Game, ci, m, 5).Game;
        Assert.Null(g.State.Workshop); // no 1410: the car is parked
        Assert.Single(g.State.Lot);
    }

    [PhrasesFact]
    public void WritesRandomJobsInTheOriginalsWords()
    {
        var problems = new List<string>();
        var book = PhraseFile.Parse(Phrases!, f => "face:" + f, problems);
        foreach (var p in problems) output.WriteLine("skipped " + p);
        output.WriteLine($"{book.Variables.Count} variables, {book.Sentences.Count} sentences, {book.Customers.Count} customers");
        var og = OriginalGame.Load(Folder!, Ai.Pack, book);
        var ci = new ContentIndex(og.Pack);
        Assert.Contains(og.Pack.Jobs, j => j.Phrased);
        int phrased = 0;
        for (uint seed = 1; seed <= 60; seed++)
        {
            var g = Sim.Game.Create(ci, "Test", seed);
            // Random jobs come once every job pack's job is done.
            g.State.CompletedJobs.AddRange(og.Pack.Jobs.Where(j => j.Sequence is not null || j.CarId is not null).Select(j => j.Id));
            g.State.FreePlay = true;
            g.State.Skill = (int)(seed % 5);
            var offer = g.RequestJob().Data!;
            if (!og.Pack.Jobs.Find(j => j.Id == offer.TemplateId)!.Phrased) continue;
            phrased++;
            var car = ci.Car(g.Vehicle(offer.VehicleId).ModelId);
            output.WriteLine($"[{offer.Difficulty} {Sim.Game.Money(offer.Fee)} {car.Name}, {offer.Reqs.Sum(r => r.SlotIds.Count)} parts, {offer.Portrait}] {offer.Text}");
            output.WriteLine($"    help: {offer.Nag}  |  thanks: {offer.Thanks}");
            Assert.DoesNotContain("%", offer.Text + offer.Nag + offer.Thanks);
        }
        Assert.True(phrased >= 8, $"only {phrased} phrased jobs");
    }

    [Fact]
    public void ReadsTgaPicturesBottomUpAndPacked()
    {
        // 2 x 2, blue and green in the bottom row, stored first (the pixels in B, G, R order).
        static byte[] Header(byte type, byte descriptor) => [0, 0, type, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 24, descriptor];
        var plain = TgaFile.Read([.. Header(2, 0), 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]);
        Assert.Equal((2, 2), (plain.Width, plain.Height));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 255, 255, 0, 0, 255, 255, 0, 255, 0, 255 }, plain.Rgba);
        // The same bottom row as two raw pixels, the top row as a run of red; stored top row first this time.
        var packed = TgaFile.Read([.. Header(10, 0x20), 0x81, 0, 0, 255, 0x01, 255, 0, 0, 0, 255, 0]);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255 }, packed.Rgba);
    }

    [OriginalFact]
    public void PaintsInTheOriginalsOwnColours()
    {
        // The Body Paint palette comes from the game's own palette picture, under our ids.
        var paints = Game.Pack.Paints;
        Assert.Equal(Ai.Pack.Paints.Select(p => p.Id), paints.Select(p => p.Id));
        Assert.Equal("#000000", paints[0].Color);
        Assert.Equal(paints.Count, paints.Select(p => p.Color).Distinct().Count());
        Assert.NotEqual(Ai.Pack.Paints.Select(p => p.Color), paints.Select(p => p.Color));
        Assert.DoesNotContain(Game.Problems, p => p.Contains("paintcolors"));
    }

    [OriginalFact]
    public void ReadsTheArchivesInMemory()
    {
        var a = OriginalArchives.Open(Folder!)!;
        var counts = new Dictionary<string, int> { ["Gfx24"] = 91, ["Sound16"] = 83, ["Scenes"] = 23, ["random"] = 59 };
        foreach (var (archive, n) in counts)
        {
            var files = a.Files(archive).ToList();
            output.WriteLine($"{archive}: {files.Count} files");
            Assert.Equal(n, files.Count);
        }
        // A screen: a 640 x 480 TGA (width and height at bytes 12..15); a sound: a RIFF/WAVE file; a scene: 3DS (4D 4D).
        var screen = a.Read("Gfx24/workshopup.tga")!;
        Assert.Equal(640, screen[12] | screen[13] << 8);
        Assert.Equal(480, screen[14] | screen[15] << 8);
        var sound = a.Read(a.Files("Sound16").First(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) is { } w ? "Sound16/" + w : "")!;
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(sound, 0, 4));
        var scene = a.Read("Scenes/carlot.3ds")!;
        Assert.Equal(0x4D4D, scene[0] | scene[1] << 8);
        Assert.Contains("%", Phrases!);
        // With a private unpacking at hand (OPENGG_EXTRACTED), every file must come out byte for byte the same.
        if (Environment.GetEnvironmentVariable("OPENGG_EXTRACTED") is { } dir && Directory.Exists(dir))
        {
            int same = 0;
            foreach (var archive in counts.Keys)
                foreach (var f in a.Files(archive))
                {
                    var disk = Path.Combine(dir, archive, f);
                    Assert.True(File.Exists(disk), disk);
                    Assert.True(File.ReadAllBytes(disk).AsSpan().SequenceEqual(a.Read(archive + "/" + f)), f);
                    same++;
                }
            output.WriteLine($"{same} files the same as the unpacked ones");
        }
    }

    [OriginalFact]
    public void TheOriginalLookNamesOnlyFilesTheArchivesHave()
    {
        // game/Skins/original.json (built into the game): every "Archive/dir/name.ext" it names must be in the archives.
        var a = OriginalArchives.Open(Folder!)!;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "game", "Skins", "original.json")));
        var named = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(System.Text.Json.JsonElement e)
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.Object) foreach (var p in e.EnumerateObject()) Walk(p.Value);
            else if (e.ValueKind == System.Text.Json.JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x);
            else if (e.ValueKind == System.Text.Json.JsonValueKind.String && e.GetString() is { } s
                     && System.Text.RegularExpressions.Regex.IsMatch(s, @"^\w+/[\w/.-]+\.\w{3}$")) named.Add(s);
        }
        Walk(doc.RootElement);
        output.WriteLine($"{named.Count} files named");
        Assert.True(named.Count > 50, $"only {named.Count} files named");
        Assert.All(named, f => Assert.True(a.Exists(f), f));
    }

    [OriginalFact]
    public void ReadsGeometryAndTextures()
    {
        var (id, info) = Game.Cars.First(c => c.Key == "orig.f150");
        var data = CarFile.ReadFull(info.Path);
        Assert.NotEmpty(data.Meshes);
        Assert.All(data.Meshes.Values, m => Assert.Equal(m.Triangles.Length / 3, m.Materials.Length));
        Assert.Contains(data.Textures, t => t is { Width: > 0 });
        var len = data.Meshes.Values.SelectMany(m => Enumerable.Range(0, m.Positions.Length / 3).Select(i => m.Positions[i * 3] + m.Pivot.X)).ToList();
        output.WriteLine($"{id}: {data.Meshes.Count} meshes, {data.Textures.Count} textures, length {len.Max() - len.Min():0.00} m");
        Assert.InRange(len.Max() - len.Min(), 3.5, 7.0);
    }

    [OriginalFact]
    public void TheAuctionsStepFollowsTheCarAsItIs()
    {
        // Measured (sessions 14 and 15): the step of cars of known make-up put on the block (by the original's part
        // numbers: a C Cab Hotrod, Snoopie's Miata, the 1967 Mustang, the 1977 Pickup and others). The model is a fit:
        // within $5 of all of them, most exact.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 3);
        decimal Step(int model, IEnumerable<int> parts, int condition = Condition.Green, double extra = 0)
        {
            var id = Game.CarsByNumber[model][0];
            var car = ci.Car(id);
            var v = g.NewVehicle(car, Owner.Player, [0, 0, 0, 1]);
            foreach (var s in car.Slots) v.Slots[s.Id] = VehicleRules.EmptySlot();
            foreach (var n in parts)
            {
                var pid = OriginalGame.PartId(id, n);
                var slot = car.Slots.First(s => s.SlotType == ci.Part(pid).SlotType);
                v.Slots[slot.Id] = VehicleRules.FilledSlot(ci, new PartInstance { PartId = pid, Condition = condition, Extra = extra }, tight: true);
            }
            return Economy.AuctionStep(ci, v);
        }
        int[] engine = [1, 2, 3, 4, 5, 7, 8, 9, 11, 12], body = [104, 101, 106, 105, 107, 108, 111, 112, 113, 114, 116, 115, 119, 120, 117, 121, 123, 124];
        int[] rg = [1001, 1002, 1004, 1006, 1005, 1007, 1009, 1008, 1003], stock = [.. engine, .. body, .. rg];
        int[] mustang = [1, 4, 5, 3, 9, 6, 8, 10, 2, 11, 12, 101, 102, 104, 103, 105, 106, 109, 110, 113, 112, 111, 1002, 1001, 1004, 1003, 1006, 1005, 1008, 1009, 1007, 1010];
        int[] pickup = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 109, 110, 100, 101, 102, 103, 106, 107, 104, 105, 108, 1004, 1005, 1006, 1007, 1009, 1008, 1003, 1002, 1000, 1001];
        var cases = new List<(string What, decimal Measured, decimal Model)>
        {
            ("C Cab green", 230, Step(10345, stock)), ("yellow", 130, Step(10345, stock, Condition.Yellow)),
            ("red", 70, Step(10345, stock, Condition.Red)), ("black", 40, Step(10345, stock, Condition.Black)),
            ("extra +0.09", 250, Step(10345, stock, extra: 0.09)), ("extra -0.09", 205, Step(10345, stock, extra: -0.09)),
            ("engine", 85, Step(10345, engine)), ("body", 135, Step(10345, body)), ("running gear", 90, Step(10345, rg)),
            ("Blower for the Air Filter, Horn", 255, Step(10345, [.. stock.Where(p => p != 12), 13, 122])),
            ("Horn added", 240, Step(10345, [.. stock, 122])), ("no Air Filter", 220, Step(10345, stock.Where(p => p != 12))),
            ("Block", 40, Step(10345, [1])), ("Blower", 45, Step(10345, [13])), ("Block + Crankshaft", 60, Step(10345, [1, 2])),
            ("the pair red", 45, Step(10345, [1, 2], Condition.Red)), ("Block + Blower", 80, Step(10345, [1, 13])),
            ("the seats", 50, Step(10345, [106, 105])), ("Block, Blower, Winscreen", 85, Step(10345, [1, 13, 114])),
            ("engine but the Air Filter", 80, Step(10345, engine[..9])), ("body but a tail light", 125, Step(10345, body[..17])),
            ("that yellow", 80, Step(10345, body[..17], Condition.Yellow)), ("that red", 50, Step(10345, body[..17], Condition.Red)),
            ("body yellow", 85, Step(10345, body, Condition.Yellow)),
            ("Miata Intake + Gauges", 100, Step(9294, [14, 138])), ("Miata Transmission", 75, Step(9294, [1007])),
            ("Miata Transmission + Block", 180, Step(9294, [1007, 10])),
            ("Mustang green", 310, Step(4, mustang)), ("Mustang red", 110, Step(4, mustang, Condition.Red)),
            ("Pickup green", 305, Step(8, pickup)), ("Pickup red", 95, Step(8, pickup, Condition.Red)),
            ("Mustang's base", 65, Step(4, [1], Condition.Black)), ("chevy truck's", 95, Step(11000, [1], Condition.Black)),
            ("Mirage's", 85, Step(8969, [1], Condition.Black)), ("Blazer's", 120, Step(1506, [3], Condition.Black)),
            ("Car03's", 25, Step(3546, [101], Condition.Black)),
        };
        foreach (var (what, measured, model) in cases) output.WriteLine($"{what}: measured {measured}, model {model}");
        Assert.All(cases, c => Assert.InRange(c.Model, c.Measured - 5, c.Measured + 5));
        Assert.True(cases.Count(c => c.Model == c.Measured) >= cases.Count * 3 / 4);
    }

    [OriginalFact]
    public void TheOriginalsSkillGoesByCashAloneOneLevelAtATime()
    {
        // Measured (session 14): a Novice with the tutorial's 9 jobs, the save's cash set to $50,000, went up to Handy and
        // then to Expert as it signed in, each with its own Skill Advance and Available Cars.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 3);
        foreach (var j in g.JobChain) g.State.CompletedJobs.Add(j.Id);
        g.State.Skill = 1;
        g.State.Stats.CarsRepaired = 9;
        g.State.Cash = 50000;
        var seen = new List<SkillEvent>();
        g.Event += e => { if (e is SkillEvent s) seen.Add(s); };
        g.CheckSkill();
        Assert.Equal([(2, "Novice"), (3, "Handy")], seen.Select(s => (s.Level, s.From)));
        Assert.Equal(3, g.State.Skill);
    }

    [OriginalFact]
    public void AJobsCarIsItsStartStateAndNothingMore()
    {
        // Measured: Escort #25 starts with its Cab Roof missing, and both windshields and the trunk (mounted on it) had to
        // be fitted too. The Checker Cab of #31 (its owner took the old engine out) starts with body and running gear.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 2);
        var escort = g.Vehicle(g.MakeScriptedJob(Game.Pack.Jobs.First(j => j.Id == "jpk.escort.25")).VehicleId);
        string SlotOf(VehicleState v, int number) => ci.Car(v.ModelId).Slots.First(s => s.SlotType == ci.Part($"{v.ModelId}.{number}").SlotType).Id;
        foreach (var n in new[] { 108, 109, 105, 110 }) Assert.Null(escort.Slots[SlotOf(escort, n)].Part);
        Assert.NotNull(escort.Slots[SlotOf(escort, 104)].Part); // the hood stays
        // Escort #24 takes its Cab Roof off, then puts the Hatchback Cosworth and the Front Windshield on it: the
        // original's car came in with its roof on (and ASSEMBLED), the Cosworth hatch in the trunk's place.
        var e24 = g.Vehicle(g.MakeScriptedJob(Game.Pack.Jobs.First(j => j.Id == "jpk.escort.24")).VehicleId);
        foreach (var n in new[] { 108, 109, 111, 112 }) Assert.NotNull(e24.Slots[SlotOf(e24, n)].Part);
        Assert.Equal(Condition.Good, e24.Slots[SlotOf(e24, 108)].Part!.Condition);
        Assert.Equal($"{e24.ModelId}.111", e24.Slots[SlotOf(e24, 111)].Part!.PartId);
        var cab = g.Vehicle(g.MakeScriptedJob(Game.Pack.Jobs.First(j => j.Id == "jpk.cab.31")).VehicleId);
        var slots = ci.Car(cab.ModelId).Slots;
        Assert.All(slots.Where(s => s.Region == Region.Engine), s => Assert.Null(cab.Slots[s.Id].Part));
        Assert.Contains(slots, s => s.Region == Region.Body && cab.Slots[s.Id].Part is not null);
    }

    [OriginalFact]
    public void PartsThatGoOnCustomPartsAreNotOnTheStockCar()
    {
        // Measured: a T-Bird's HotRod Dual Carbs and Blower (unmarked, on the custom HotRod manifold) and a Fairmont's
        // Boosters (unmarked, on the custom Booster Support) were not on the job cars; the jobs had them bought and fitted.
        var ci = new ContentIndex(Game.Pack);
        foreach (var (car, names) in new[] { ("orig.tbird", new[] { "HotRod Dual Carbs", "HotRod Blower", "Front Windshield ChopTop" }), ("orig.fairmont", new[] { "Right Booster", "Left Booster" }) })
        {
            var model = ci.Car(car);
            foreach (var n in names)
            {
                var part = ci.Pack.Parts.First(p => p.Name == n && p.Id.StartsWith(car + ".", StringComparison.Ordinal));
                var slot = model.Slots.First(s => s.SlotType == part.SlotType);
                Assert.False(slot.Required, $"{car} {n}");
                Assert.NotEqual(part.Id, slot.DefaultPart);
            }
            var v = Sim.Game.Create(ci, "Test", 1).NewVehicle(model, Owner.Player, AllGood);
            Assert.DoesNotContain(v.Slots.Values, st => st.Part is { } p && names.Contains(ci.Part(p.PartId).Name));
            Assert.Contains(v.Slots.Values, st => st.Part is { } p && ci.Part(p.PartId).Name == (car == "orig.tbird" ? "Carb" : "Cab Roof"));
        }
    }

    [OriginalFact]
    public void ANewJunkyardShelfHoldsWhatTheCarThatCameInHasNot()
    {
        // Measured on the original's 22 new shelves: a car won at the Auction brings one of each stock part it came
        // without and one of each custom part (with what goes on them), in the order of the car's file, never an accessory,
        // then 15 to 21 parts at random; a job's car the custom parts it has not, not the stock parts its owner took off
        // (the Checker Cab of #31 got its Hemi kit, not its engine); a car of a model with a shelf adds nothing.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 4);
        g.State.Skill = 4;
        List<string> Expected(VehicleState v, bool won)
        {
            var car = ci.Car(v.ModelId);
            var on = v.Slots.Values.Where(s => s.Part is not null).Select(s => s.Part!.PartId).ToHashSet();
            return ci.Pack.Parts.Where(p => car.Slots.Any(s => s.SlotType == p.SlotType) && p.SoundKind != PartSound.Accessory
                && ((p.Custom || p.AddOn) && !on.Contains(p.Id) || won && car.Slots.Any(s => s.DefaultPart == p.Id && v.Slots[s.Id].Part is null)))
                .Select(p => p.Id).ToList();
        }
        void Check(VehicleState v, bool won)
        {
            var ids = g.State.Shelves[v.ModelId].Select(j => j.Part.PartId).ToList();
            var expected = Expected(v, won);
            Assert.Equal(expected, ids.Take(expected.Count).ToList());
            Assert.InRange(ids.Count - expected.Count, 15, 21);
            Assert.DoesNotContain(ids, id => ci.Part(id).SoundKind == PartSound.Accessory);
        }
        for (int i = 0; i < 5; i++)
        {
            var had = g.State.Shelves.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            var v = BuyAndPark(g);
            if (had.TryGetValue(v.ModelId, out var n)) Assert.Equal(n, g.State.Shelves[v.ModelId].Count);
            else Check(v, won: true);
        }
        var job = g.MakeScriptedJob(Game.Pack.Jobs.First(j => j.Id == "jpk.cab.31"));
        var cab = g.Vehicle(job.VehicleId);
        g.State.Offer = job;
        Assert.True(g.AcceptJob().Ok);
        Check(cab, won: false);
        var shelf = g.State.Shelves[cab.ModelId];
        Assert.Contains(shelf, j => ci.Part(j.Part.PartId).Name == "Hemi V-8 Block");
        Assert.DoesNotContain(shelf.Take(Expected(cab, false).Count), j => ci.Part(j.Part.PartId).Name == "Straight 6 block");
    }

    [OriginalFact]
    public void ADecalGoesIntoThePaintPictureButNotOnBareMetal()
    {
        // Measured: a Star on a new Hood went into the car's paint picture, in the colour picked for it (the whole-panel
        // brush afterwards painted over it); on a panel in bare metal it left nothing, one use gone all the same.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 3);
        OwnCarInWorkshop(g, "orig.mustang");
        var v = g.WorkshopVehicle()!;
        var canvas = g.Canvas(v)!;
        var d = ci.Pack.Decals.First(x => x.Tint && x.Uses >= 3);
        g.State.Cash = 1000;
        Assert.True(g.BuyDecal(d.Id).Ok);
        var panel = ci.Car(v.ModelId).Slots.First(s => s.Region == Region.Body && v.Slots[s.Id].Part is not null);
        var red = new Rgb(164, 36, 32);
        Assert.True(g.StampDecal(d.Id, [new DecalTexel(10, 12, red, panel.Id), new DecalTexel(11, 12, red, null)]).Ok);
        Assert.Equal(red, canvas.At(10, 12));
        Assert.Equal(red, canvas.At(11, 12));
        Assert.Equal(d.Uses - 1, g.DecalUses(d.Id));
        SetCondition(g, panel.Id, Condition.Red);
        var before = canvas.At(20, 20);
        Assert.True(g.StampDecal(d.Id, [new DecalTexel(20, 20, red, panel.Id)]).Ok);
        Assert.Equal(before, canvas.At(20, 20));
        Assert.Equal(d.Uses - 2, g.DecalUses(d.Id));
        Assert.True(g.StampDecal(d.Id, []).Ok); // nothing under it that takes paint
        Assert.Equal(d.Uses - 3, g.DecalUses(d.Id));
    }

    [OriginalFact]
    public void StartsTheEngineAsTheOriginalDoes()
    {
        // Measured: a whole engine starts, a yellow part in it too; a red part (its block aside) clicks, as no starter
        // does, whatever is missing too; else a part it needs missing, or a red block, cranks without catching (an F350
        // with a red block and a new starter cranked, and again with all the rest of its engine new); the exhaust and
        // the body change nothing.
        var ci = new ContentIndex(Game.Pack);
        var g = Sim.Game.Create(ci, "Test", 3);
        OwnCarInWorkshop(g, "orig.mustang");
        var v = g.WorkshopVehicle()!;
        var slots = ci.Car(v.ModelId).Slots;
        EngineOutcome Outcome() => Economy.Diagnose(ci, v).Outcome;
        Assert.Equal(EngineOutcome.Runs, Outcome());
        bool IsBlock(SlotDef s) => v.Slots[s.Id].Part is { } p && ci.Part(p.PartId).Block;
        var starter = slots.Single(s => s.Family == "starter");
        var block = slots.Single(s => s.Region == Region.Engine && IsBlock(s));
        var engine = slots.Where(s => s.Region == Region.Engine && s.Required && s != starter && s != block).ToList();
        var (other, gone) = (engine[0], engine[1]);
        output.WriteLine($"starter {starter.Name}, block {block.Name}, other {other.Name}, taken off {gone.Name}");
        SetCondition(g, other.Id, Condition.Yellow);
        Assert.Equal(EngineOutcome.Runs, Outcome());
        SetCondition(g, other.Id, Condition.Red);
        Assert.Equal(EngineOutcome.NoCrank, Outcome());
        var part = v.Slots[gone.Id].Part;
        v.Slots[gone.Id].Part = null;
        Assert.Equal(EngineOutcome.NoCrank, Outcome()); // the red part first
        SetCondition(g, other.Id, Condition.Green);
        Assert.Equal(EngineOutcome.NoStart, Outcome());
        SetCondition(g, block.Id, Condition.Red);
        Assert.Equal(EngineOutcome.NoStart, Outcome());
        v.Slots[gone.Id].Part = part;
        Assert.Equal(EngineOutcome.NoStart, Outcome()); // the whole engine, only its block red: it cranks
        SetCondition(g, block.Id, Condition.Black);
        Assert.Equal(EngineOutcome.NoStart, Outcome());
        SetCondition(g, block.Id, Condition.Yellow);
        Assert.Equal(EngineOutcome.Runs, Outcome());
        SetCondition(g, block.Id, Condition.Green);
        part = v.Slots[starter.Id].Part;
        v.Slots[starter.Id].Part = null;
        Assert.Equal(EngineOutcome.NoCrank, Outcome());
        v.Slots[starter.Id].Part = part;
        foreach (var s in slots.Where(s => s.Region != Region.Engine && v.Slots[s.Id].Part is not null)) SetCondition(g, s.Id, Condition.Black);
        Assert.Equal(EngineOutcome.Runs, Outcome());
    }
}
