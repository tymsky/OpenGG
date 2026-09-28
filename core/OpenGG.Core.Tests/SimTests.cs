using OpenGG.Core.Content;
using OpenGG.Core.Sim;
using static OpenGG.Core.Tests.TestUtil;

namespace OpenGG.Core.Tests;

public class ContentTests
{
    [Fact]
    public void AiPackIsInternallyConsistent() => Assert.Empty(Validation.Validate(Ai.Pack));

    [Fact]
    public void TheTestPackWithTwoModelsIsInternallyConsistent() => Assert.Empty(Validation.Validate(TwoModels.Pack));

    [Fact]
    public void EverySlotHasParentsResolvedFromJson()
    {
        var hood = Ai.Slot("sedan", "body.hood");
        Assert.Empty(hood.Parents);
        Assert.Contains(Ai.Pack.Cars.SelectMany(c => c.Slots), s => s.Parents.Count > 0);
    }
}

public class AssemblyTests
{
    [Theory]
    [InlineData("sedan")]
    public void CarCanBeTakenApartAndPutBackTogether(string carId)
    {
        var g = Game.Create(Ai, "Test", 1234);
        OwnCarInWorkshop(g, carId);
        var v = g.WorkshopVehicle()!;
        var car = Ai.Car(carId);
        var uidBySlot = car.Slots.Where(s => v.Slots[s.Id].Part is not null).ToDictionary(s => s.Id, s => v.Slots[s.Id].Part!.Uid);
        var order = DisassembleAll(g);
        Assert.DoesNotContain(car.Slots, s => v.Slots[s.Id].Part is not null);
        Assert.Equal(uidBySlot.Count, order.Count);
        Reassemble(g, order, id => uidBySlot[id]);
        Assert.True(VehicleRules.GetCompleteness(Ai, v).Done);
        Assert.Empty(g.State.Bin);
    }

    [Fact]
    public void RejectsTheWrongTool()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        Assert.Equal("wrong_tool", g.UseFastener("wheel.fl", 0, "paint").Code);
        Assert.True(g.UseFastener("wheel.fl", 0, "ratchet").Ok);
    }

    [Fact]
    public void NeedsTheHoodOpenToReachTheEngineBay()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        Assert.Equal("closed", g.UseFastener("elec.battery", 0, "ratchet").Code);
        g.ToggleOpen("body.hood");
        Assert.True(g.UseFastener("elec.battery", 0, "ratchet").Ok);
    }

    [Fact]
    public void BlocksPartsHiddenBehindOthers()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        var r = g.UseFastener("brake.caliper.fl", 0, "ratchet");
        Assert.Equal("blocked", r.Code);
        Assert.Equal(["wheel.fl"], r.Slots!);
    }

    [Fact]
    public void OilPanNeedsTheFlywheelOutWhichNeedsTheTransmissionOut()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        Assert.Equal(["eng.flywheel"], g.UseFastener("eng.oil_pan", 0, "ratchet").Slots!);
        Assert.Equal(["drivetrain.transmission"], g.UseFastener("eng.flywheel", 0, "ratchet").Slots!);
    }

    [Fact]
    public void RoofComesOffOnlyAfterBothWindshields()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        Assert.Equal(["body.windshield", "body.rear_window"], g.RemovePart("body.roof").Slots!);
        Assert.True(g.RemovePart("body.windshield").Ok);
        Assert.True(g.RemovePart("body.rear_window").Ok);
        Assert.True(g.RemovePart("body.roof").Ok);
    }

    [Fact]
    public void PartGoesOnLooseAndMustBeBoltedDown()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        g.RemovePart("wheel.fl");
        var uid = g.State.Bin[0].Part.Uid;
        Assert.True(g.InstallPart("wheel.fl", uid).Ok);
        Assert.False(VehicleRules.IsComplete(Ai, g.WorkshopVehicle()!));
        g.UseAllFasteners("wheel.fl", "ratchet", remove: false);
        Assert.True(VehicleRules.IsComplete(Ai, g.WorkshopVehicle()!));
    }

    [Fact]
    public void ABlackPartDoesNotGoBackOn()
    {
        // Measured: a black fender let go over the car got "Assembly Error!" and stayed in the Parts Bin.
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        Assert.True(g.RemovePart("wheel.fl").Ok);
        var item = g.State.Bin[0];
        item.Part.Condition = Condition.Black;
        var r = g.InstallPart("wheel.fl", item.Part.Uid);
        Assert.Equal("worn_out", r.Code);
        Assert.Null(g.WorkshopVehicle()!.Slots["wheel.fl"].Part);
        Assert.Same(item, Assert.Single(g.State.Bin));
        item.Part.Condition = Condition.Red;
        Assert.True(g.InstallPart("wheel.fl", item.Part.Uid).Ok);
    }

    [Fact]
    public void CancellingTheBoltsPutsThePartBackInItsPlaceInTheBin()
    {
        // Measured: CANCEL while a part's bolts are being done up takes it off the car and back into the Parts Bin.
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        foreach (var s in new[] { "wheel.fl", "wheel.fr" })
        {
            g.UseAllFasteners(s, "ratchet", remove: true);
            Assert.True(g.RemovePart(s).Ok);
        }
        var item = g.State.Bin[0];
        Assert.True(g.InstallPart("wheel.fl", item.Part.Uid).Ok);
        Assert.True(g.UseFastener("wheel.fl", 0, "ratchet").Ok);
        Assert.True(g.CancelAttach("wheel.fl", 0, item.From).Ok);
        Assert.Null(g.WorkshopVehicle()!.Slots["wheel.fl"].Part);
        Assert.Equal(2, g.State.Bin.Count);
        Assert.Equal(item.Part.Uid, g.State.Bin[0].Part.Uid);
        Assert.Equal(item.From, g.State.Bin[0].From);
    }

    [Fact]
    public void PartMountedOnSeveralPartsNeedsAllOfThem()
    {
        // The original's cars can mount one part on several others. Make the rear window
        // hang on both the roof and the windshield.
        var pack = Json.Parse<ContentPack>(Json.Write(Ai.Pack));
        pack.Cars.First(c => c.Id == "sedan").Slots.First(s => s.Id == "body.rear_window").Parents.Add("body.windshield");
        var ci = new ContentIndex(pack);
        var g = Game.Create(ci, "Test", 1);
        OwnCarInWorkshop(g, "sedan");

        var r = g.RemovePart("body.windshield");
        Assert.Equal("children", r.Code);
        Assert.Equal(["body.rear_window"], r.Slots!);
        var rear = g.RemovePart("body.rear_window").Data!;
        Assert.True(g.RemovePart("body.windshield").Ok);
        var put = g.InstallPart("body.rear_window", rear);
        Assert.Equal("no_parent", put.Code);
        Assert.Equal(["body.windshield"], put.Slots!);
    }
}

public class ConditionTests
{
    static (Game g, BinItem item) WheelInBin()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        g.RemovePart("wheel.fl");
        return (g, g.State.Bin[0]);
    }

    [Fact]
    public void ACarOfYoursInTopConditionGetsCarCompleteOnceAndItsClockStops()
    {
        // Measured: "Car Complete" as the last bolt of the last part goes in (a yellow Alternator repaired and bolted
        // back), once for a car; its Repair Time stays as it was after that.
        var g = Game.Create(Ai, "Test", 1);
        var done = new List<CarCompleteEvent>();
        g.Event += e => { if (e is CarCompleteEvent c) done.Add(c); };
        var id = OwnCarInWorkshop(g, "sedan", [0, 0, 1, 0]);
        var v = g.Vehicle(id);
        Assert.Empty(done); // yellow parts: not in top condition
        foreach (var st in v.Slots.Values) if (st.Part is { } p) p.Condition = Condition.Green;
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        g.RemovePart("wheel.fl");
        Assert.Empty(done); // a part missing
        g.Tick(5);
        Assert.True(g.InstallPart("wheel.fl", g.State.Bin[0].Part.Uid).Ok);
        Assert.Empty(done); // on but loose
        g.UseAllFasteners("wheel.fl", "ratchet", remove: false);
        var c = Assert.Single(done);
        Assert.Equal(id, c.VehicleId);
        Assert.Equal(5, c.Seconds, 3);
        Assert.True(v.Completed);
        g.Tick(60);
        Assert.Equal(5, v.Stats.RepairTime, 3);
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        g.RemovePart("wheel.fl");
        g.InstallPart("wheel.fl", g.State.Bin[0].Part.Uid);
        g.UseAllFasteners("wheel.fl", "ratchet", remove: false);
        Assert.Single(done); // once for a car
    }

    [Fact]
    public void ACarsClockRunsOnlyWhileTheWorkshopIsUpWithoutABox()
    {
        // Measured: 30 s in the Catalog, with the Scrap Part box up and in the JunkYard added nothing to the car's Repair
        // Time; the sign-in's TOTAL TIME counted all of it.
        var g = Game.Create(Ai, "Test", 1);
        var v = g.Vehicle(OwnCarInWorkshop(g, "sedan", [0, 1, 0, 0]));
        double play = g.State.PlayTime;
        g.Tick(30);
        g.Tick(30, atWork: false);
        Assert.Equal(30, v.Stats.RepairTime, 3);
        Assert.Equal(play + 60, g.State.PlayTime, 3);
    }

    [Theory]
    [InlineData(0, "0 second")]
    [InlineData(41.2, "41 seconds")]
    [InlineData(60, "1 minute and 0 second")]
    [InlineData(3723, "1 hour and 2 minutes and 3 seconds")]
    [InlineData(7201, "2 hours and 1 second")]
    public void CarCompleteSaysTheTimeAsTheOriginalDoes(double seconds, string words) =>
        Assert.Equal(words, CarCompleteWords.Took(seconds));

    [Fact]
    public void RepairsRedAndYellowToGreenNeverBlack()
    {
        var (g, it) = WheelInBin();
        it.Part.Condition = Condition.Red;
        it.Part.Extra = 0.05;
        var cash = g.State.Cash;
        var r = g.RepairItem(it.Part.Uid);
        Assert.True(r.Ok);
        Assert.Equal(Condition.Green, it.Part.Condition);
        Assert.Equal(0.05, it.Part.Extra); // the extra stays with the part
        Assert.Equal(cash - r.Data, g.State.Cash);
        it.Part.Condition = Condition.Black;
        Assert.Equal("broken", g.RepairItem(it.Part.Uid).Code);
    }

    [Fact]
    public void ScrappedBlackPartsShowUpInTheJunkyardAsRed()
    {
        var (g, it) = WheelInBin();
        it.Part.Condition = Condition.Black;
        g.ScrapItem(it.Part.Uid);
        var back = g.Junk.Find(j => j.Part.Uid == it.Part.Uid);
        Assert.NotNull(back);
        Assert.Equal(Condition.Red, back!.Part.Condition);
        Assert.True(g.BuyJunk(back.Id).Ok);
        g.LeaveJunkyard();
        Assert.Contains(g.State.Bin, b => b.Part.Uid == it.Part.Uid);
    }

    [Fact]
    public void EachCarModelHasItsOwnJunkyardShelf()
    {
        var m = TwoModels;
        var g = Game.Create(m, "Test", 1);
        OwnCarInWorkshop(g, "twin");
        Assert.Equal("twin", g.State.JunkFor);
        var types = m.Car("twin").Slots.Select(s => s.SlotType).ToHashSet();
        // Measured: a new shelf holds one of each custom part the car has not, then 15 to 21 parts at random.
        var car = g.WorkshopVehicle()!;
        int customs = m.Pack.Parts.Count(p => p.Custom && types.Contains(p.SlotType) && !car.Slots.Values.Any(s => s.Part?.PartId == p.Id));
        Assert.InRange(g.Junk.Count, customs + m.Pack.Rules.JunkShelfRandom[0], customs + m.Pack.Rules.JunkShelfRandom[^1]);
        foreach (var j in g.Junk)
        {
            Assert.Contains(m.Part(j.Part.PartId).SlotType, types);
            Assert.NotEqual(Condition.Black, j.Part.Condition); // a new shelf's black stock is red by the time the yard shows it
        }
        var first = g.Junk.Select(j => j.Id).ToList();
        g.PutCarInLot();
        OwnCarInWorkshop(g, "sedan");
        Assert.DoesNotContain(g.Junk, j => first.Contains(j.Id));
        Assert.Equal(first, g.Shelf("twin").Select(j => j.Id).ToList());
    }

    [Fact]
    public void GoingToTheJunkyardChangesTheShelfShown()
    {
        // Measured (28 visits): before the yard comes up its black parts turn red, 0 to 3 of its parts go and 0 to 3 new
        // ones come at its end, in any colour; the other models' shelves stay as they are.
        var g = Game.Create(TwoModels, "Test", 5);
        OwnCarInWorkshop(g, "twin");
        var other = g.Shelf("sedan").Select(j => j.Id).ToList();
        g.Junk[0].Part.Condition = Condition.Black;
        int came = 0, went = 0, cameBlack = 0;
        for (int visit = 0; visit < 60; visit++)
        {
            var before = g.Junk.Select(j => j.Id).ToList();
            var blacks = g.Junk.Where(j => j.Part.Condition == Condition.Black).Select(j => j.Id).ToHashSet();
            var now = g.EnterJunkyard();
            var ids = now.Select(j => j.Id).ToList();
            var kept = before.Where(ids.Contains).ToList();
            Assert.Equal(kept, ids.Take(kept.Count)); // what stays keeps its order, and the new ones come after it
            Assert.InRange(before.Count - kept.Count, 0, 3);
            Assert.InRange(ids.Count - kept.Count, 0, 3);
            Assert.All(now.Where(j => blacks.Contains(j.Id)), j => Assert.Equal(Condition.Red, j.Part.Condition));
            cameBlack += now.Skip(kept.Count).Count(j => j.Part.Condition == Condition.Black);
            came += ids.Count - kept.Count;
            went += before.Count - kept.Count;
        }
        Assert.True(came > 0 && went > 0);
        Assert.True(cameBlack > 0); // a new part can be black, at a black part's price until the next visit
        Assert.Equal(other, g.Shelf("sedan").Select(j => j.Id).ToList());
    }
}

/// <summary>The formulas measured in the original (prices to the cent, 2026-09-25).</summary>
public class PriceTests
{
    static PartDef Def(decimal min, decimal max) => new() { Id = "x", Name = "X", Price = max, PriceMin = min };
    static PartInstance Part(int condition, double extra) => new() { PartId = "x", Condition = condition, Extra = extra };

    [Theory]
    [InlineData(35, 200, 2, -0.024277473, 106.55)] // Intake Manifold
    [InlineData(8, 88, 3, -0.02341685, 60.08)] // Cooling Fan
    [InlineData(40, 210, 1, 0.081304364, 80.85)] // Cab Roof: black in the save, red as the yard came up
    [InlineData(150, 550, 3, 0.048942536, 429.72)] // Flatbed: green above 1
    [InlineData(9, 90, 1, -0.05577868, 26.00)] // Front Right Fender
    [InlineData(10, 150, 1, -0.078612626, 38.67)] // wheel: black in the save, red as the yard came up
    [InlineData(10, 100, 0, 0.001, 10.00)] // Starter: black, come with the visit
    [InlineData(10, 40, 2, -0.023770865, 23.02)] // Right Shock
    public void JunkyardPriceAndScrapOffer(decimal min, decimal max, int condition, double extra, decimal shown)
    {
        var ci = new ContentIndex(new ContentPack { Parts = [Def(min, max)] });
        Assert.Equal(shown, Economy.Cents(Economy.JunkPrice(ci, Part(condition, extra))));
        if (condition > Condition.Black) Assert.Equal(shown, Economy.Cents(Economy.ScrapValue(ci, Part(condition, extra))));
    }

    [Fact]
    public void ABlackPartIsWorthItsMinimum()
    {
        // Measured on four job cars: SCRAP offered $10.00 for black $10–100 parts; REPAIR refuses them.
        var ci = new ContentIndex(new ContentPack { Parts = [Def(10, 100)] });
        Assert.Equal(10.00m, Economy.Cents(Economy.ScrapValue(ci, Part(Condition.Black, 0.05))));
        Assert.Equal(0m, Economy.RepairCost(ci, Part(Condition.Black, 0.05)));
    }

    [Theory]
    [InlineData(10, 150, 1, -0.078612626, 48.50)]
    [InlineData(10, 40, 2, -0.023770865, 5.24)]
    public void RepairCost(decimal min, decimal max, int condition, double extra, decimal shown)
    {
        var pack = new ContentPack { Parts = [Def(min, max)] };
        Assert.Equal(shown, Economy.Cents(Economy.RepairCost(new ContentIndex(pack), Part(condition, extra))));
    }

    [Fact]
    public void RepairedPartIsWorthAGreenOneWithItsExtra()
    {
        // The shock above, repaired: the JunkYard then offered $29.52.
        Assert.Equal(29.52m, Economy.Cents(Economy.PartValue(Def(10, 40), Part(Condition.Green, -0.023770865))));
    }

    [Fact]
    public void ChargesAndPaysToTheCent()
    {
        // The measured game dropped the cents of charges; we take that for a compatibility bug.
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        var uid = g.RemovePart("wheel.fl").Data!;
        var part = g.Item(uid)!.Part;
        part.Condition = Condition.Red;
        part.Extra = 0.037;
        var cost = Economy.Cents(Economy.RepairCost(Ai, part));
        Assert.NotEqual(Math.Floor(cost), cost);
        var cash = g.State.Cash;
        Assert.Equal(cost, g.RepairItem(uid).Data);
        Assert.Equal(cash - cost, g.State.Cash);
        cash = g.State.Cash;
        var value = g.ScrapItem(uid).Data;
        Assert.Equal(Economy.Cents(Economy.PartValue(Ai, part)), value);
        Assert.Equal(cash + value, g.State.Cash);
    }

    [Theory]
    [InlineData(77.37, Difficulty.Easy)]
    [InlineData(225, Difficulty.Easy)]
    [InlineData(245.63, Difficulty.Easy)]
    [InlineData(250, Difficulty.Medium)]
    [InlineData(325, Difficulty.Medium)]
    [InlineData(548.22, Difficulty.Medium)]
    [InlineData(553.67, Difficulty.Hard)]
    [InlineData(1049.90, Difficulty.Hard)]
    [InlineData(1050.31, Difficulty.Expert)]
    [InlineData(12555, Difficulty.Expert)]
    public void DifficultyWordFollowsTheFee(decimal fee, Difficulty expected) =>
        Assert.Equal(expected, Economy.DifficultyOf(Ai.Pack.Rules, fee));

    [Fact]
    public void NewCatalogPartsAreGreenWithARandomExtra()
    {
        var g = Game.Create(Ai, "Test", 1);
        g.State.Cash = 1_000_000;
        var partId = Ai.Pack.Parts[0].Id;
        for (int i = 0; i < 20; i++)
        {
            var uid = g.BuyPart(partId).Data!;
            var p = g.Item(uid)!.Part;
            Assert.Equal(Condition.Green, p.Condition);
            Assert.InRange(p.Extra, -Ai.Pack.Rules.ConditionSpread, Ai.Pack.Rules.ConditionSpread);
        }
    }
}

public class JobTests
{
    static Game SimpleJob(uint seedStart)
    {
        for (uint seed = seedStart; seed < seedStart + 200; seed++)
        {
            var g = Game.Create(Ai, "Test", seed);
            var r = g.RequestJob();
            if (r.Ok && r.Data!.Reqs.All(q => q.Type != JobReqType.Missing)) return g;
        }
        throw new InvalidOperationException("no simple job found");
    }

    /// <summary>These tests take the whole car apart and put it back: a black part would not go back on (measured), so
    /// the ones the job does not have replaced are made red first.</summary>
    static void NoBlackLeft(Game g, IEnumerable<string> replacedSlots)
    {
        var replaced = replacedSlots.ToHashSet();
        foreach (var (id, st) in g.WorkshopVehicle()!.Slots)
            if (st.Part is { Condition: Condition.Black } p && !replaced.Contains(id)) p.Condition = Condition.Red;
    }

    [Fact]
    public void PaysTheFeePlusWhatIsLeftOfTheBudget()
    {
        var g = SimpleJob(10);
        var offer = g.State.Offer!;
        Assert.True(offer.Fee > 0);
        Assert.True(g.AcceptJob().Ok);
        Assert.Equal(offer.VehicleId, g.State.Workshop);
        Assert.False(g.JobProgress().Done);
        NoBlackLeft(g, offer.Reqs.SelectMany(q => q.SlotIds));
        var v = g.WorkshopVehicle()!;
        var uid = v.Slots.Where(kv => kv.Value.Part is not null).ToDictionary(kv => kv.Key, kv => kv.Value.Part!.Uid);
        var order = DisassembleAll(g);
        decimal spent = 0;
        foreach (var req in offer.Reqs)
            foreach (var slotId in req.SlotIds)
            {
                var slot = Ai.Slot(v.ModelId, slotId);
                var partId = req.Type == JobReqType.Install ? req.Parts!.First(p => Ai.Part(p).SlotType == slot.SlotType) : slot.DefaultPart!;
                spent += Economy.Charge(Ai.Part(partId).Price);
                uid[slotId] = g.BuyPart(partId).Data!;
            }
        Assert.Equal(offer.Fee - spent, g.State.Job!.Budget);
        g.State.Cash += 0.73m; // the payout keeps the cents of your cash
        var cash = g.State.Cash;
        Reassemble(g, order, id => uid[id]);
        Assert.Null(g.State.Job);
        Assert.Null(g.State.Workshop);
        Assert.Equal(cash + offer.Fee + (offer.Fee - spent), g.State.Cash);
        Assert.Equal(1, g.State.Stats.JobsDone);
    }

    [Fact]
    public void AFinishedJobsCarIsStillThereAsTheJobEnds()
    {
        // The UI shows the car as the job leaves it, the last part on, before it goes.
        var g = SimpleJob(10);
        var offer = g.State.Offer!;
        g.AcceptJob();
        NoBlackLeft(g, offer.Reqs.SelectMany(q => q.SlotIds));
        var v = g.WorkshopVehicle()!;
        var uid = v.Slots.Where(kv => kv.Value.Part is not null).ToDictionary(kv => kv.Key, kv => kv.Value.Part!.Uid);
        var order = DisassembleAll(g);
        foreach (var req in offer.Reqs)
            foreach (var slotId in req.SlotIds)
            {
                var slot = Ai.Slot(v.ModelId, slotId);
                var partId = req.Type == JobReqType.Install ? req.Parts!.First(p => Ai.Part(p).SlotType == slot.SlotType) : slot.DefaultPart!;
                uid[slotId] = g.BuyPart(partId).Data!;
            }
        bool seen = false;
        g.Event += e =>
        {
            if (e is JobDoneEvent j)
                seen = g.State.Workshop == j.Job.VehicleId && VehicleRules.IsComplete(Ai, g.Vehicle(j.Job.VehicleId));
        };
        Reassemble(g, order, id => uid[id]);
        Assert.True(seen);
        Assert.False(g.State.Vehicles.ContainsKey(offer.VehicleId));
        Assert.Null(g.State.Workshop);
    }

    [Fact]
    public void FeesCoverNewPartsForTheRequest()
    {
        for (uint seed = 1; seed <= 60; seed++)
        {
            var g = Game.Create(Ai, "Test", seed);
            g.State.Skill = (int)(seed % (uint)Ai.Pack.Rules.Skills.Count);
            var offer = g.RequestJob().Data!;
            var car = Ai.Car(g.Vehicle(offer.VehicleId).ModelId);
            decimal parts = 0;
            foreach (var req in offer.Reqs)
                foreach (var id in req.SlotIds)
                {
                    var slot = car.Slots.First(s => s.Id == id);
                    var prices = req.Type == JobReqType.Install
                        ? req.Parts!.Where(p => Ai.Part(p).SlotType == slot.SlotType).Select(p => Ai.Part(p).Price)
                        : [Ai.Part(slot.DefaultPart!).Price];
                    parts += prices.Min();
                }
            Assert.True(offer.Fee > parts, $"seed {seed}: fee {offer.Fee} <= parts {parts}");
        }
    }

    [Fact]
    public void CancelSendsARandomCustomerAwayQuittingGivesTheCarBack()
    {
        // Measured: a random offer is new on every Get A Job (a job pack's comes back until taken).
        var g = Game.Create(Ai, "Test", 3);
        var first = g.RequestJob().Data!;
        Assert.True(g.DeclineJob().Ok);
        Assert.False(g.State.Vehicles.ContainsKey(first.VehicleId));
        var a = g.RequestJob().Data!;
        Assert.NotSame(first, a);
        Assert.True(g.State.Vehicles.ContainsKey(a.VehicleId));
        g.AcceptJob();
        g.QuitJob();
        Assert.Null(g.State.Job);
        Assert.Null(g.State.Workshop);
        Assert.False(g.State.Vehicles.ContainsKey(a.VehicleId));
        Assert.Equal(1, g.State.Stats.JobsQuit);
    }

    [Fact]
    public void AShelfFirstStockedForAJobGivenUpIsNotKept()
    {
        // Measured: six new models' yards visited during jobs then given up, none saved; a finished job's is.
        var g = Game.Create(Ai, "Test", 3);
        g.State.FreePlay = true;
        var quit = g.RequestJob().Data!;
        g.AcceptJob();
        var model = g.Vehicle(quit.VehicleId).ModelId;
        Assert.Contains(model, g.State.Shelves.Keys); // made as the car came in (an Escort's, with no visit to the yard)
        _ = g.OpenJunkyard();
        g.QuitJob();
        Assert.DoesNotContain(model, g.State.Shelves.Keys);

        var left = g.RequestJob().Data!;
        g.AcceptJob();
        var other = g.Vehicle(left.VehicleId).ModelId;
        _ = g.OpenJunkyard();
        Assert.True(g.LeaveJob().Ok);
        Assert.DoesNotContain(other, g.State.Shelves.Keys);

        // A shelf that was there before the job stays.
        var kept = g.RequestJob().Data!;
        g.AcceptJob();
        var third = g.Vehicle(kept.VehicleId).ModelId;
        g.State.Job!.NewShelves = null;
        _ = g.Shelf(third);
        g.State.Job!.NewShelves = null; // as if stocked before the job
        g.QuitJob();
        Assert.Contains(third, g.State.Shelves.Keys);
    }

    [Fact]
    public void OnlyOffersJobsAllowedAtYourSkillLevel()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var g = Game.Create(Ai, "Test", seed);
            var offer = g.RequestJob().Data!;
            Assert.Equal(Difficulty.Easy, Ai.Pack.Jobs.First(j => j.Id == offer.TemplateId).Difficulty);
            Assert.Equal(Economy.DifficultyOf(Ai.Pack.Rules, offer.Fee), offer.Difficulty);
        }
    }

    [Fact]
    public void HintsComeRoundInTurn()
    {
        var g = Game.Create(Ai, "Test", 3);
        g.RequestJob();
        g.AcceptJob();
        g.State.Job!.Hints = ["one", "two"];
        Assert.Equal(["one", "two", "one", "two"], Enumerable.Range(0, 4).Select(_ => g.NextHint()).ToList());
    }
}

public class EngineTests
{
    [Fact]
    public void TellsYouWhatIsWrongByHowItSounds()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        EngineOutcome Outcome() => Economy.Diagnose(Ai, g.WorkshopVehicle()!).Outcome;
        Assert.Equal(EngineOutcome.Runs, Outcome());
        SetCondition(g, "exh.muffler", Condition.Red);
        Assert.Equal(EngineOutcome.Loud, Outcome());
        SetCondition(g, "ign.plug2", Condition.Black);
        Assert.Equal(EngineOutcome.Rough, Outcome());
        SetCondition(g, "ign.coil", Condition.Black);
        Assert.Equal(EngineOutcome.NoStart, Outcome());
        SetCondition(g, "elec.starter", Condition.Black);
        Assert.Equal(EngineOutcome.NoCrank, Outcome());
        SetCondition(g, "elec.battery", Condition.Black);
        Assert.Equal(EngineOutcome.Dead, Outcome());
    }
}

public class AuctionTests
{
    [Fact]
    public void AskingDropsTwentyFiveEveryTwoSecondsAndTheCarGoesAfterTwentyAndAHalf()
    {
        var g = Game.Create(Ai, "Test", 3);
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = 0;
        var opening = a.CurrentBid;
        Assert.Equal(opening + a.Step - 25, a.Asking); // the first drop comes as the car goes up
        g.Tick(1.9);
        Assert.Equal(opening + a.Step - 25, a.Asking);
        g.Tick(0.2);
        Assert.Equal(opening + a.Step - 50, a.Asking);
        g.Tick(18.3);
        Assert.True(a.Open);
        g.Tick(0.2);
        Assert.False(a.Open);
        Assert.NotSame(a, g.State.Auction); // nobody bid: the next car comes up
    }

    [Fact]
    public void AuctionCarsComeStrippedWithNothingLeftHanging()
    {
        // Measured on 18 cars bought: at Novice every one fairly whole (10 to 15 % of the stock car gone), at Expert and
        // Mekada every one bought badly beaten up (41 to 79 % gone, found in about 57 % of the cars seen); nothing on a
        // car hanging on a missing part.
        double[] Gone(int skill)
        {
            var g = Game.Create(Ai, "Test", 5);
            g.State.Skill = skill;
            var shares = new List<double>();
            for (int i = 0; i < 60; i++)
            {
                var a = g.NextAuctionCar().Data!;
                var v = g.Vehicle(a.VehicleId);
                var car = Ai.Car(v.ModelId);
                var stock = car.Slots.Where(s => s.DefaultPart is not null).ToList();
                shares.Add((double)stock.Count(s => v.Slots[s.Id].Part is null) / stock.Count);
                Assert.DoesNotContain(car.Slots, s => v.Slots[s.Id].Part is not null && s.Parents.Any(p => v.Slots[p].Part is null));
            }
            return [.. shares];
        }
        Assert.All(Gone(1), x => Assert.InRange(x, 0.05, 0.25));
        var expert = Gone(3);
        Assert.InRange(expert.Count(x => x > 0.35) / (double)expert.Length, 0.5, 0.8);
        Assert.Contains(expert, x => x < 0.25);
    }

    [Fact]
    public void TheNextCarDrivesOnBeforeItsClockStarts()
    {
        // Measured: after Skip Car (or a car's time running out) the car drives off, the next one drives on, and its
        // figures come up about 0.75 s later with the first $25 drop.
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        var first = g.NextAuctionCar().Data!;
        Assert.Equal(0, first.Arriving);
        var a = g.SkipCar().Data!;
        Assert.NotSame(first, a);
        Assert.Equal(Game.AuctionSwap, a.Arriving);
        Assert.Equal(a.CurrentBid + a.Step, a.Asking); // no drop yet
        Assert.Equal("arriving", g.PlaceBid().Code);
        Assert.Same(a, g.SkipCar().Data); // nothing to skip while it drives on
        a.RivalMax = 0;
        g.Tick(0.5);
        Assert.Equal(0, a.Elapsed);
        g.Tick(0.3);
        Assert.Equal(0, a.Arriving);
        Assert.Equal(a.CurrentBid + a.Step - 25, a.Asking);
        Assert.Equal(0.05, a.Elapsed, 3);
        Assert.True(g.PlaceBid().Ok);
    }

    [Fact]
    public void ACarKeepsItsStepWhenItComesBackUnlessItChanged()
    {
        // Measured: bought at a step of 95 and sold at 95; another 70, then 75 after it was repaired.
        var g = Game.Create(Ai, "Test", 11);
        var v = BuyAndPark(g);
        var bought = v.AuctionStep;
        Assert.NotNull(bought);
        Assert.Equal(bought, g.AuctionOwnCar(v.Id).Data!.Step);
        g.State.Auction!.RivalMax = 0;
        for (int t = 0; t < 30 && g.State.Auction!.Open; t++) g.Tick(1);
        g.LeaveAuction();
        Assert.Equal(Owner.Player, v.Owner);
        Assert.Equal(bought, v.AuctionStep);
        foreach (var st in v.Slots.Values.Where(st => st.Part is not null)) st.Part!.Condition = Condition.Red;
        var value = Economy.CarValue(Ai, v);
        Assert.NotEqual(value, v.AuctionStepValue);
        g.AuctionOwnCar(v.Id);
        Assert.Equal(value, v.AuctionStepValue); // a step drawn anew, for the car as it is now
    }

    [Fact]
    public void EveryBidIsTheAskingPriceAndRaisesItByTheCarsStep()
    {
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = 0;
        var asking = a.Asking;
        Assert.True(g.PlaceBid().Ok);
        Assert.Equal(asking, a.CurrentBid);
        Assert.Equal(asking + a.Step, a.Asking);
        Assert.Equal(Bidder.Player, a.Leader);
    }

    [Fact]
    public void AWonCarGoesStraightToTheWorkshop()
    {
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = 0;
        Assert.True(g.PlaceBid().Ok);
        for (int t = 0; t < 30 && a.Open; t++) g.Tick(1);
        Assert.Equal(Bidder.Player, a.Result?.Buyer);
        Assert.Equal(a.VehicleId, g.State.Workshop);
        Assert.Equal(a.CurrentBid, g.Vehicle(a.VehicleId).Stats.OrigCost);
        Assert.Equal(1_000_000 - a.CurrentBid, g.State.Cash);
    }

    [Fact]
    public void AWonCarTakesTheWorkshopAndTheCarThereGoesToTheLot()
    {
        // Measured: the car in the WorkShop went to the Car Lot when another was won; the one won came in.
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        string Win()
        {
            var a = g.NextAuctionCar().Data!;
            a.RivalMax = 0;
            Assert.True(g.PlaceBid().Ok);
            for (int t = 0; t < 30 && a.Open; t++) g.Tick(1);
            Assert.Equal(Bidder.Player, a.Result?.Buyer);
            g.LeaveAuction();
            return a.VehicleId;
        }
        var first = Win();
        var second = Win();
        Assert.Equal(second, g.State.Workshop);
        Assert.Contains(first, g.State.Lot);
        Assert.Equal(2, g.CarsOwned);
    }

    [Fact]
    public void RivalsBidUpToTheirLimit()
    {
        var g = Game.Create(Ai, "Test", 4);
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = a.Asking + a.Step * 5;
        for (int t = 0; t < 100 && a.Open; t++) g.Tick(0.5);
        Assert.Equal(Bidder.Rival, a.Result?.Buyer);
        Assert.True(a.CurrentBid <= a.RivalMax);
        Assert.All(a.History, b => Assert.Equal(Bidder.Rival, b.Who));
        Assert.False(g.State.Vehicles.ContainsKey(a.VehicleId));
    }

    [Fact]
    public void RivalsOpenAtOnceAndBidAgainTheMomentADropBringsThePriceBack()
    {
        // Measured: in most cars the first bid came a tenth of a second or two after the car went up; the last
        // ones came right after the drops that brought the asking price back to what the bidders pay.
        int quick = 0;
        for (uint seed = 1; seed <= 40; seed++)
        {
            var g = Game.Create(Ai, "Test", seed);
            var a = g.NextAuctionCar().Data!;
            a.RivalMax = 1_000_000;
            for (int i = 0; i < 25 && a.History.Count == 0; i++) g.Tick(0.01);
            if (a.History.Count > 0) quick++;
        }
        Assert.InRange(quick, 20, 38);

        var h = Game.Create(Ai, "Test", 9);
        var b = h.NextAuctionCar().Data!;
        b.RivalMax = b.Asking - 10; // just under the asking price: they wait
        h.Tick(1.5);
        Assert.Empty(b.History);
        Assert.True(b.RivalsWaiting);
        h.Tick(0.6); // the drop at 2 s brings the asking price under what they pay
        var bid = Assert.Single(b.History);
        Assert.InRange(bid.At, 2.0, 2.07);
    }

    [Fact]
    public void PlaceBidWhileLeadingRaisesYourOwnBid()
    {
        // Measured: leading at $720 with the asking price at $775, Place Bid made it $775 (asking $855, the step 80).
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = 0;
        Assert.True(g.PlaceBid().Ok);
        var first = a.CurrentBid;
        Assert.Equal(Bidder.Player, a.Leader);
        Assert.True(g.PlaceBid().Ok);
        Assert.Equal(first + a.Step, a.CurrentBid);
        Assert.Equal(a.CurrentBid + a.Step, a.Asking);
        Assert.Equal(Bidder.Player, a.Leader);
    }

    [Fact]
    public void TheAskingPriceNeverDropsBelowTheBidAndOneDrop()
    {
        // Measured: 585 with the asking price at 640 -> 615 -> 610, and there it stayed.
        var g = Game.Create(Ai, "Test", 3);
        g.State.Cash = 1_000_000;
        var a = g.NextAuctionCar().Data!;
        a.RivalMax = 0;
        Assert.True(g.PlaceBid().Ok);
        a.Step = 60;
        a.Asking = a.CurrentBid + a.Step;
        g.Tick(2.1);
        Assert.Equal(a.CurrentBid + 35, a.Asking);
        for (int t = 0; t < 8; t++) g.Tick(1);
        Assert.Equal(a.CurrentBid + 25, a.Asking);
    }

    [Theory]
    [InlineData(825, 40)]
    [InlineData(4150, 205)]
    [InlineData(899, 40)]
    [InlineData(100, 5)]
    public void BuyingYourOwnCarBackCostsOnlyAServiceFee(int bid, int fee)
    {
        // Measured: outbidding everybody for your own car calls the sale off ("Sale Cancelled"): a fee of $40 at $825 and
        // $205 at $4150 came off the cash, and the car stayed yours with its costs as they were.
        var g = Game.Create(Ai, "Test", 5);
        g.State.Cash = 1_000_000;
        var id = OwnCarInWorkshop(g, "sedan");
        var v = g.Vehicle(id);
        v.Stats.OrigCost = 2450;
        v.Stats.RepairCost = 12;
        var a = g.AuctionOwnCar(id).Data!;
        a.RivalMax = 0;
        Assert.True(g.PlaceBid().Ok);
        a.CurrentBid = bid;
        for (int t = 0; t < 30 && a.Open; t++) g.Tick(1);
        Assert.Equal(Bidder.Player, a.Result?.Buyer);
        Assert.Equal(fee, a.Result!.Fee);
        Assert.Equal(1_000_000 - fee, g.State.Cash);
        Assert.Equal(Owner.Player, v.Owner);
        Assert.Equal(2450, v.Stats.OrigCost);
        Assert.Equal(12, v.Stats.RepairCost);
        Assert.Equal(id, g.State.Workshop);
    }

    [Fact]
    public void TheRivalsLimitIsDrawnFromTheSpreadAtYourSkill()
    {
        // Measured: at Novice the other bidders went 9.8 to 15.1 steps above the opening (25 cars), at Mekada 3.0 to 14.5
        // (43 cars, half under 8.6), and a stripped car fetched far more than its parts are worth.
        List<double> Steps(int skill)
        {
            var g = Game.Create(Ai, "Test", 7);
            g.State.Skill = skill;
            var list = new List<double>();
            for (int i = 0; i < 200; i++)
            {
                var a = g.NextAuctionCar().Data!;
                list.Add((double)((a.RivalMax - a.CurrentBid) / a.Step));
            }
            return list;
        }
        var novice = Steps(1);
        Assert.All(novice, x => Assert.InRange(x, 9.8, 15.1));
        Assert.InRange(novice.OrderBy(x => x).ElementAt(100), 13.3, 14.3);
        var mekada = Steps(4);
        Assert.All(mekada, x => Assert.InRange(x, 3.0, 14.5));
        Assert.InRange(mekada.OrderBy(x => x).ElementAt(100), 8.0, 9.2);
    }

    [Fact]
    public void AuctioningYourOwnCarPaysYouAndReportsTheProfit()
    {
        var g = Game.Create(Ai, "Test", 5);
        var id = OwnCarInWorkshop(g, "sedan");
        g.Vehicle(id).Stats.OrigCost = 1000;
        g.Vehicle(id).Stats.RepairCost = 1;
        g.PutCarInLot();
        var a = g.AuctionOwnCar(id).Data!;
        a.RivalMax = 5000;
        var cash = g.State.Cash;
        for (int t = 0; t < 100 && a.Open; t++) g.Tick(0.5);
        Assert.Equal(Bidder.Rival, a.Result?.Buyer);
        Assert.Equal(cash + a.CurrentBid, g.State.Cash);
        Assert.Equal(a.CurrentBid - 1001, a.Result!.Profit);
    }
}

public class CarLotTests
{
    [Fact]
    public void CarsAreNumberedInTheOrderTheyWereBoughtAndNumbersAreNotReused()
    {
        // Measured: the first car bought is Number 0, then 1, 2 ...; a sold car's Number is not given again.
        var g = Game.Create(Ai, "Test", 11);
        var cars = Enumerable.Range(0, 3).Select(_ => BuyAndPark(g)).ToList();
        Assert.Equal([0, 1, 2], cars.Select(v => v.Number!.Value));
        SellToRival(g, cars[1].Id);
        var next = BuyAndPark(g);
        Assert.Equal(3, next.Number);
        Assert.Equal([0, 2, 3], g.State.Lot.Select(id => g.Vehicle(id).Number!.Value));
        var again = Game.Load(Ai, g.Save());
        Assert.Equal(4, again.State.Stats.CarsBought);
    }

    [Fact]
    public void TheLotKeepsYourCarsInTheOrderOfTheirNumbers()
    {
        var g = Game.Create(Ai, "Test", 12);
        var cars = Enumerable.Range(0, 3).Select(_ => BuyAndPark(g)).ToList();
        int[] Numbers() => g.State.Lot.Select(id => g.Vehicle(id).Number!.Value).ToArray();
        Assert.True(g.BringToWorkshop(cars[0].Id).Ok);
        Assert.Equal([1, 2], Numbers());
        Assert.True(g.PutCarInLot().Ok);
        Assert.Equal([0, 1, 2], Numbers()); // back in the first bay
        Assert.True(g.BringToWorkshop(cars[1].Id).Ok);
        Assert.True(g.BringToWorkshop(cars[2].Id).Ok); // the one in the WorkShop swaps places with it
        Assert.Equal([0, 1], Numbers());
        Assert.Equal(cars[2].Id, g.State.Workshop);
    }

    [Fact]
    public void AFullCarLotClosesTheAuction()
    {
        // Measured: with twelve cars parked, Go To Auction says the lot is full.
        var g = Game.Create(Ai, "Test", 13);
        var cars = Enumerable.Range(0, Game.LotBays).Select(_ => BuyAndPark(g)).ToList();
        var full = g.GoToAuction();
        Assert.False(full.Ok);
        Assert.Equal("lot_full", full.Code);
        Assert.Null(g.State.Auction);
        Assert.True(g.BringToWorkshop(cars[5].Id).Ok);
        Assert.Equal("lot_full", g.GoToAuction().Code); // a car you win would have nowhere to go
        SellToRival(g, cars[5].Id);
        Assert.Equal(Game.LotBays - 1, g.CarsOwned);
        Assert.True(g.GoToAuction().Ok);
    }

    [Fact]
    public void OldSavesNumberTheirCarsInLotOrder()
    {
        var g = Game.Create(Ai, "Test", 14);
        var a = g.NewVehicle(g.CI.Car("sedan"), Owner.Player, AllGood);
        var b = g.NewVehicle(g.CI.Car("sedan"), Owner.Player, AllGood);
        g.State.Lot.AddRange([b.Id, a.Id]);
        g.State.Stats.CarsBought = 2;
        var loaded = Game.Load(Ai, g.Save());
        Assert.Equal(2, loaded.Vehicle(b.Id).Number);
        Assert.Equal(3, loaded.Vehicle(a.Id).Number);
        Assert.Equal(4, loaded.State.Stats.CarsBought);
    }
}

public class ProgressTests
{
    static void SellOwnCar(Game g, decimal price)
    {
        var id = OwnCarInWorkshop(g, "sedan");
        g.PutCarInLot();
        var a = g.AuctionOwnCar(id).Data!;
        a.RivalMax = price + 1000;
        a.Asking = price;
        for (int t = 0; t < 100 && a.Open; t++) g.Tick(0.5);
    }

    [Fact]
    public void NewMechanicsStartAsLearningWithFiveThousand()
    {
        var g = Game.Create(Ai, "Test", 1);
        Assert.Equal(0, g.State.Skill);
        Assert.Equal("Learning", g.SkillName);
        Assert.Equal(5000m, g.State.Cash);
    }

    [Fact]
    public void SkillNeedsCashAndCarsRepaired()
    {
        var g = Game.Create(Ai, "Test", 1);
        SellOwnCar(g, 30000);
        Assert.True(g.State.Cash > 35000);
        Assert.Equal(1, g.State.Stats.CarsRepaired);
        Assert.Equal(0, g.State.Skill); // rich, but one car is not enough

        g.State.Stats.CarsRepaired = 9;
        SellOwnCar(g, 1000);
        Assert.Equal(10, g.State.Stats.CarsRepaired);
        Assert.Equal(2, g.State.Skill); // Handy: $15,000 and 8 cars; Expert needs 15 cars
    }
}

public class ShopTests
{
    [Fact]
    public void JunkyardPartsAreBoughtOnTheSpotAndCanBeGivenBack()
    {
        var g = Game.Create(Ai, "Test", 2);
        OwnCarInWorkshop(g, "sedan");
        var item = g.Junk[0];
        var price = Economy.JunkPrice(Ai, item.Part);
        var cash = g.State.Cash;
        Assert.True(g.BuyJunk(item.Id).Ok);
        Assert.Equal(cash - Economy.Cents(price), g.State.Cash);
        Assert.Contains(item, g.State.PurchaseBin);
        Assert.DoesNotContain(item, g.Junk);
        Assert.True(g.ReturnJunk(item.Id).Ok);
        Assert.Equal(cash, g.State.Cash);
        Assert.Contains(item, g.Junk);
        g.BuyJunk(item.Id);
        Assert.Equal(1, g.LeaveJunkyard());
        Assert.Contains(g.State.Bin, b => b.Part.Uid == item.Part.Uid);
        Assert.Empty(g.State.PurchaseBin);
    }

    [Fact]
    public void TheWorkshopShowsTheBinOfItsCarsModelAndNothingWhenItIsEmpty()
    {
        // Measured: a job on another model shows that model's bin; with the WorkShop empty the bin is empty.
        var g = Game.Create(TwoModels, "Test", 2);
        OwnCarInWorkshop(g, "sedan");
        g.BuyPart("hood_sedan");
        g.BuyPart("hood_sedan_twin");
        Assert.Equal(["hood_sedan"], g.BinFor(g.WorkshopVehicle()!.ModelId).Select(b => b.Part.PartId));
        Assert.Equal(["hood_sedan_twin"], g.BinFor("twin").Select(b => b.Part.PartId));
        g.PutCarInLot();
        Assert.Empty(g.BinFor(g.WorkshopVehicle()?.ModelId));
        Assert.Equal(2, g.State.Bin.Count);
    }

    [Fact]
    public void CannotBuyMoreThanYouCanPay()
    {
        var g = Game.Create(Ai, "Test", 2);
        OwnCarInWorkshop(g, "sedan");
        var item = g.Junk.First(j => Economy.JunkPrice(Ai, j.Part) >= 2);
        g.State.Cash = Economy.Cents(Economy.JunkPrice(Ai, item.Part)) - 0.01m;
        Assert.False(g.BuyJunk(item.Id).Ok);
    }

    [Fact]
    public void ScrappedPartsGoOnTheShelfForWhatTheyFetched()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        var uid = g.RemovePart("wheel.fl").Data!;
        var value = g.ScrapItem(uid).Data;
        var back = g.Junk.Find(j => j.Part.Uid == uid)!;
        Assert.Equal(Condition.Green, back.Part.Condition);
        Assert.Equal(value, Economy.Cents(Economy.JunkPrice(Ai, back.Part)));
    }

    [Fact]
    public void DecalsComeWithAFixedNumberOfUses()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        var d = Ai.Pack.Decals[0];
        Assert.False(g.AddDecal(d.Id, Vec3.Zero, Vec3.Up, 0, 0.4).Ok);
        Assert.True(g.BuyDecal(d.Id).Ok);
        Assert.Equal(d.Uses, g.DecalUses(d.Id));
        for (int i = 0; i < d.Uses; i++) Assert.True(g.AddDecal(d.Id, Vec3.Zero, Vec3.Up, 0, 0.4, color: "#ab2624").Ok);
        Assert.False(g.AddDecal(d.Id, Vec3.Zero, Vec3.Up, 0, 0.4).Ok);
        Assert.Equal(d.Uses, g.WorkshopVehicle()!.Decals.Count);
        Assert.Equal("#ab2624", g.WorkshopVehicle()!.Decals[0].Color);
    }

    [Fact]
    public void PaintAndDecalsLeaveNoMarkOnAPanelInBareMetal()
    {
        // Measured: paint on a red, a yellow and a black panel, and a decal on a red one, left nothing; the decal's use
        // was gone all the same.
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        var v = g.WorkshopVehicle()!;
        var paint = Ai.Pack.Paints[1].Id;
        foreach (var c in new[] { Condition.Red, Condition.Yellow, Condition.Black })
        {
            SetCondition(g, "body.hood", c);
            Assert.True(g.PaintPart("body.hood", paint).Ok);
            Assert.Null(v.Slots["body.hood"].Part!.Color);
        }
        SetCondition(g, "body.hood", Condition.Green);
        Assert.True(g.PaintPart("body.hood", paint).Ok);
        Assert.NotNull(v.Slots["body.hood"].Part!.Color);

        var d = Ai.Pack.Decals[0];
        g.BuyDecal(d.Id);
        SetCondition(g, "body.door_f_l", Condition.Red);
        Assert.True(g.AddDecal(d.Id, Vec3.Zero, Vec3.Up, 0, 0.4, slotId: "body.door_f_l").Ok);
        Assert.Empty(v.Decals);
        Assert.Equal(d.Uses - 1, g.DecalUses(d.Id));
        Assert.True(g.AddDecal(d.Id, Vec3.Zero, Vec3.Up, 0, 0.4, slotId: "body.hood").Ok);
        Assert.Single(v.Decals);
    }

    [Fact]
    public void MoneySpentOnYourCarShowsInTheCarLot()
    {
        var g = Game.Create(Ai, "Test", 1);
        var id = OwnCarInWorkshop(g, "sedan");
        var d = Ai.Pack.Decals[0];
        g.BuyDecal(d.Id);
        Assert.Equal(Economy.Cents(d.Price), g.Vehicle(id).Stats.RepairCost);
    }
}

public class AssembledTagTests
{
    [Fact]
    public void ShowsOnlyWhenNothingIsMissingColouredByTheWorstPart()
    {
        var g = Game.Create(Ai, "Test", 1);
        OwnCarInWorkshop(g, "sedan");
        var v = g.WorkshopVehicle()!;
        Assert.Equal(Condition.Green, VehicleRules.AssembledCondition(Ai, v));
        SetCondition(g, "wheel.fl", Condition.Yellow);
        Assert.Equal(Condition.Yellow, VehicleRules.AssembledCondition(Ai, v));
        Assert.Equal(Condition.Green, VehicleRules.AssembledCondition(Ai, v, Region.Engine));
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        Assert.Null(VehicleRules.AssembledCondition(Ai, v, Region.RunningGear)); // loose
        // Measured: while a part is being unscrewed the tag stays (a wheel with a bolt out: red tag up).
        Assert.Equal(Condition.Yellow, VehicleRules.AssembledCondition(Ai, v, Region.RunningGear, unscrewing: "wheel.fl"));
        g.RemovePart("wheel.fl");
        Assert.Null(VehicleRules.AssembledCondition(Ai, v));
        Assert.Null(VehicleRules.AssembledCondition(Ai, v, unscrewing: "wheel.fl"));
    }
}

public class RestartTests
{
    [Fact]
    public void RestartBringsTheJobRequestBackWithAFullBudget()
    {
        var g = Game.Create(Ai, "Test", 10);
        var offer = g.RequestJob().Data!;
        g.AcceptJob();
        var v = g.WorkshopVehicle()!;
        var before = v.Slots.ToDictionary(kv => kv.Key, kv => (kv.Value.Part?.PartId, kv.Value.Part?.Condition));
        var slot = offer.Reqs[0].SlotIds[0];
        g.BuyPart(Ai.Slot(v.ModelId, slot).DefaultPart!);
        g.UseAllFasteners("wheel.fl", "ratchet", remove: true);
        Assert.True(g.State.Job!.Budget < offer.Fee);

        var again = g.RestartJob().Data!;
        Assert.Null(g.State.Job);
        Assert.Null(g.State.Workshop);
        Assert.Same(again, g.State.Offer);
        Assert.Equal(offer.Fee, again.Budget);
        Assert.Empty(g.State.Bin);
        Assert.False(g.State.Vehicles.ContainsKey(v.Id));
        var car = g.Vehicle(again.VehicleId);
        Assert.Equal(before, car.Slots.ToDictionary(kv => kv.Key, kv => (kv.Value.Part?.PartId, kv.Value.Part?.Condition)));
        Assert.True(car.Slots.Values.All(s => s.Part is null || s.Fasteners.All(f => f)));
        Assert.True(g.AcceptJob().Ok);
    }
}

public class SaveTests
{
    [Fact]
    public void RoundTrips()
    {
        var g = Game.Create(Ai, "Test", 42);
        g.RequestJob();
        g.AcceptJob();
        _ = g.Junk;
        var copy = Game.Load(Ai, g.Save());
        Assert.Equal(g.Save(), copy.Save());
    }

    [Fact]
    public void RngContinuesWhereItStopped()
    {
        var g = Game.Create(Ai, "Test", 7);
        g.Rng.Next();
        var copy = Game.Load(Ai, g.Save());
        Assert.Equal(g.Rng.Next(), copy.Rng.Next());
    }

    [Fact]
    public void LoadsASaveWhoseContentChanged()
    {
        // The player's copy of the original changed under a save: a car file gone, a part gone, a car made over.
        var g = Game.Create(Ai, "Test", 42);
        var parked = BuyAndPark(g);
        var own = g.Vehicle(OwnCarInWorkshop(g, "sedan"));
        own.Slots["wheel.fl"].Part!.PartId = "no.such.part";
        own.Slots["no.such.slot"] = new SlotState { Part = new PartInstance { Uid = "moved", PartId = own.Slots["wheel.fr"].Part!.PartId } };
        own.Slots.Remove("wheel.rr");
        own.Slots["wheel.fr"].Fasteners.Add(true);
        parked.ModelId = "no.such.car";
        g.State.Bin.Add(new BinItem { Part = new PartInstance { Uid = "lost", PartId = "no.such.part" } });
        g.State.Shelves["no.such.car"] = [];
        g.State.DecalUses["no.such.decal"] = 3;

        var loaded = Game.Load(Ai, g.Save());
        var s = loaded.State;
        Assert.NotEmpty(loaded.Problems);
        Assert.DoesNotContain(parked.Id, s.Vehicles.Keys);
        Assert.DoesNotContain(parked.Id, s.Lot);
        var v = loaded.WorkshopVehicle()!;
        Assert.Null(v.Slots["wheel.fl"].Part);
        Assert.Null(v.Slots["wheel.rr"].Part);
        Assert.DoesNotContain("no.such.slot", v.Slots.Keys);
        Assert.Equal(Ai.Part(v.Slots["wheel.fr"].Part!.PartId).Fasteners.Count, v.Slots["wheel.fr"].Fasteners.Count);
        Assert.Contains(s.Bin, b => b.Part.Uid == "moved");
        Assert.DoesNotContain(s.Bin, b => b.Part.Uid == "lost");
        Assert.DoesNotContain("no.such.car", s.Shelves.Keys);
        Assert.DoesNotContain("no.such.decal", s.DecalUses.Keys);
        // Everything that asks the content about the car works again.
        Assert.False(VehicleRules.GetCompleteness(Ai, v).Done);
        _ = Economy.Diagnose(Ai, v);
        _ = Economy.CarValue(Ai, v);
        Assert.Equal(loaded.Save(), Game.Load(Ai, loaded.Save()).Save());
    }
}

public class PaintCanvasTests
{
    [Fact]
    public void DabsStayInsideTheirPanelsSquare()
    {
        var c = PaintCanvas.Filled(new Rgb(10, 20, 30));
        var red = new Rgb(200, 0, 0);
        // A big dab at the corner of the square (0.25..0.42, 0..0.17) must not spill outside it.
        c.Dab(0.25, 0.0, 20, red, (0.25, 0.0, 0.42, 0.17));
        Assert.Equal(red, c.At(64, 0));
        Assert.Equal(red, c.At(70, 5));
        Assert.Equal(new Rgb(10, 20, 30), c.At(63, 0));
    }

    [Fact]
    public void RoundTripsThroughTheSave()
    {
        var c = PaintCanvas.Filled(Rgb.FromHex("#ab2624"));
        c.FillUv(0.5, 0.5, 0.67, 0.67, Rgb.FromHex("#f7f60e"));
        var back = PaintCanvas.Decode(c.Encode())!;
        Assert.Equal(c.Pixels, back.Pixels);
        Assert.True(c.Encode().Length < 2000); // a few flat colours pack small
    }
}

public class ScriptedJobTests
{
    [Fact]
    public void ACarGreenJobCountsOnlyTheRegionsItsTabsAllow()
    {
        // A job on the ENGINE tab alone that asks for "the car green" (as the original's Escort overheating job does)
        // means the parts you can work on.
        var g = Game.Create(Ai, "Test", 5);
        g.State.FreePlay = true;
        var tpl = new JobTemplateDef
        {
            Id = "test.engine_only",
            CarId = "sedan",
            Fee = 80,
            Tabs = [View.Complete, View.Engine],
            Start =
            [
                new JobStatDef { Region = "all", Condition = Condition.Green },
                new JobStatDef { Part = "hood_sedan", Condition = JobStatDef.Absent },
                new JobStatDef { Part = "battery", Condition = Condition.Red },
            ],
            Complete = [new JobStatDef { Region = "all", Condition = Condition.Green }],
        };
        g.State.Offer = g.MakeScriptedJob(tpl);
        Assert.True(g.AcceptJob().Ok);
        var v = g.WorkshopVehicle()!;
        Assert.Null(v.Slots["body.hood"].Part);
        Assert.Equal(["Battery: not fixed"], g.JobProgress().Open);
        g.UseAllFasteners("elec.battery", "ratchet", remove: true);
        var uid = g.RemovePart("elec.battery").Data!;
        Assert.True(g.RepairItem(uid).Ok);
        Assert.True(g.InstallPart("elec.battery", uid).Ok);
        g.UseAllFasteners("elec.battery", "ratchet", remove: false);
        Assert.Null(g.State.Job); // done, the hood still missing
        Assert.Equal(1, g.State.Stats.JobsDone);
    }
}

public class DecalStampTests
{
    static DecalSample?[] Grid(int w, int h, Func<int, int, DecalSample?> at)
    {
        var grid = new DecalSample?[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                grid[j * w + i] = at(i, j);
        return grid;
    }

    static readonly Rgb Red = new(200, 0, 0);

    [Fact]
    public void EachScreenPixelPaintsThePixelItLandsOn()
    {
        var texels = DecalStamp.Texels(4, 3, Grid(4, 3, (i, j) => new DecalSample(30.5 + i, 40.5 + j, Red, "hood", 1)));
        Assert.Equal(12, texels.Count);
        Assert.Contains(texels, t => t.X == 33 && t.Y == 42 && t.SlotId == "hood");
    }

    [Fact]
    public void WhereThePictureIsFinerThanTheScreenTheGapsAreFilled()
    {
        // Screen pixels landing 3 of the paint picture's pixels apart: the original leaves the paint between them in dots
        // (seen on a tailgate); OpenGG fills the whole square they span, each pixel in the colour of the nearest.
        var blue = new Rgb(0, 0, 200);
        var texels = DecalStamp.Texels(4, 4, Grid(4, 4, (i, j) => new DecalSample(10.5 + 3 * i, 20.5 + 3 * j, i < 2 ? Red : blue, null, 1)));
        var at = texels.ToDictionary(t => (t.X, t.Y), t => t.Color);
        for (int y = 20; y <= 29; y++)
            for (int x = 10; x <= 19; x++)
                Assert.True(at.ContainsKey((x, y)), $"{x},{y} left out");
        Assert.Equal(100, at.Count);
        Assert.Equal(Red, at[(11, 21)]);
        Assert.Equal(blue, at[(18, 28)]);
    }

    [Fact]
    public void NothingIsFilledAcrossSurfacesOrWideGaps()
    {
        var two = DecalStamp.Texels(2, 4, Grid(2, 4, (i, j) => new DecalSample(10.5 + 3 * i, 20.5 + 3 * j, Red, null, j < 2 ? 1 : 2)));
        Assert.DoesNotContain(two, t => t.Y is 24 or 25);
        var apart = DecalStamp.Texels(2, 2, Grid(2, 2, (i, j) => new DecalSample(10.5 + 20 * i, 20.5 + 3 * j, Red, null, 1)));
        Assert.Equal(4, apart.Count);
        var hole = DecalStamp.Texels(3, 3, Grid(3, 3, (i, j) => i == 1 && j == 1 ? null : new DecalSample(10.5 + 3 * i, 20.5 + 3 * j, Red, null, 1)));
        Assert.DoesNotContain(hole, t => t.X == 14 && t.Y == 24); // the clear middle of the decal stays clear
    }
}
