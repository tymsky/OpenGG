using OpenGG.Core.Content;
using OpenGG.Core.Sim;

namespace OpenGG.Core.Tests;

public static class TestUtil
{
    static ContentIndex? ai;

    /// <summary>The repo root (the folder with OpenGG.sln).</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenGG.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("OpenGG.sln not found above the test binaries.");
        }
    }

    /// <summary>The generated ai pack (run <c>npm run gen:assets</c> first).</summary>
    public static ContentIndex Ai
    {
        get
        {
            if (ai is not null) return ai;
            var dir = Path.Combine(RepoRoot, "data", "ai");
            if (!File.Exists(Path.Combine(dir, PackLoader.AiManifest)))
                throw new FileNotFoundException("The ai pack is missing. Run `npm run gen:assets` in the repo root first.", dir);
            return ai = new ContentIndex(PackLoader.LoadDirectory(dir).Pack);
        }
    }

    static ContentIndex? twoModels;

    /// <summary>The ai pack with a second car model, "twin": the sedan's copy with its own parts and slot types (ids
    /// ending in "_twin"). The beta's pack has one car; this tests what the game keeps per model (JunkYard shelves,
    /// Parts Bins) as the original's many cars need it.</summary>
    public static ContentIndex TwoModels
    {
        get
        {
            if (twoModels is not null) return twoModels;
            var pack = Json.Parse<ContentPack>(Json.Write(Ai.Pack));
            var sedan = pack.Cars.Single(c => c.Id == "sedan");
            var types = sedan.Slots.Select(s => s.SlotType).ToHashSet();
            var twin = Json.Parse<CarModelDef>(Json.Write(sedan));
            twin.Id = "twin";
            foreach (var s in twin.Slots)
            {
                s.SlotType += "_twin";
                if (s.DefaultPart is not null) s.DefaultPart += "_twin";
            }
            var parts = pack.Parts.Where(p => types.Contains(p.SlotType)).Select(p => Json.Parse<PartDef>(Json.Write(p))).ToList();
            foreach (var p in parts)
            {
                p.Id += "_twin";
                p.SlotType += "_twin";
            }
            pack.Cars.Add(twin);
            pack.Parts.AddRange(parts);
            return twoModels = new ContentIndex(pack);
        }
    }

    public static readonly double[] AllGood = [0, 0, 0, 1];

    /// <summary>Give the player a car (condition odds <paramref name="wear"/>) and put it in the workshop.</summary>
    public static string OwnCarInWorkshop(Game g, string carId, double[]? wear = null)
    {
        var v = g.NewVehicle(g.CI.Car(carId), Owner.Player, wear ?? AllGood);
        g.State.Lot.Add(v.Id);
        var r = g.BringToWorkshop(v.Id);
        Assert.True(r.Ok, r.Msg);
        return v.Id;
    }

    /// <summary>Buy the next car at the Auction (nobody else bids) and park it in the Car Lot.</summary>
    public static VehicleState BuyAndPark(Game g)
    {
        g.State.Cash = Math.Max(g.State.Cash, 1_000_000);
        var go = g.GoToAuction();
        Assert.True(go.Ok, go.Msg);
        var a = go.Data!;
        a.RivalMax = 0;
        Assert.True(g.PlaceBid().Ok);
        for (int t = 0; t < 30 && a.Open; t++) g.Tick(1);
        g.LeaveAuction();
        Assert.Equal(a.VehicleId, g.State.Workshop);
        Assert.True(g.PutCarInLot().Ok);
        return g.Vehicle(a.VehicleId);
    }

    /// <summary>Put one of your cars on the block and let somebody else buy it.</summary>
    public static void SellToRival(Game g, string vehicleId)
    {
        var a = g.AuctionOwnCar(vehicleId).Data!;
        a.RivalMax = 1_000_000;
        for (int t = 0; t < 100 && a.Open; t++) g.Tick(0.5);
        Assert.Equal(Bidder.Rival, a.Result?.Buyer);
        g.LeaveAuction();
    }

    /// <summary>Take the workshop car apart completely. Returns the slots in removal order.</summary>
    public static List<string> DisassembleAll(Game g)
    {
        var v = g.WorkshopVehicle()!;
        var car = g.CI.Car(v.ModelId);
        var order = new List<string>();
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var s in car.Slots)
            {
                var st = v.Slots[s.Id];
                if (st.Part is null) continue;
                foreach (var b in s.BlockedBy.Prepend(s.Id))
                {
                    var bd = g.CI.Slot(car.Id, b);
                    if (bd.Openable is not null && v.Slots[b].Part is not null && !v.Slots[b].Open) g.ToggleOpen(b);
                }
                var def = g.CI.Part(st.Part.PartId);
                for (int i = 0; i < def.Fasteners.Count; i++)
                {
                    if (v.Slots[s.Id].Part is null || !v.Slots[s.Id].Fasteners[i]) continue;
                    var tool = g.CI.ToolForKind(def.Fasteners[i].Kind)!;
                    if (g.UseFastener(s.Id, i, tool.Id).Ok) progress = true;
                }
                if (v.Slots[s.Id].Part is not null && g.RemovePart(s.Id).Ok)
                {
                    order.Add(s.Id);
                    progress = true;
                }
            }
        }
        return order;
    }

    /// <summary>Put parts back in reverse removal order, tightening everything.</summary>
    public static void Reassemble(Game g, List<string> order, Func<string, string> uidFor)
    {
        for (int k = order.Count - 1; k >= 0; k--)
        {
            string slotId = order[k];
            var v = g.WorkshopVehicle();
            if (v is null) return; // the job finished
            var r = g.InstallPart(slotId, uidFor(slotId));
            Assert.True(r.Ok, $"install {slotId}: {r.Msg}");
            var def = g.CI.Part(v.Slots[slotId].Part!.PartId);
            for (int i = 0; i < def.Fasteners.Count; i++)
            {
                if (g.WorkshopVehicle() is null) return;
                var tool = g.CI.ToolForKind(def.Fasteners[i].Kind)!;
                var t = g.UseFastener(slotId, i, tool.Id);
                Assert.True(t.Ok, $"tighten {slotId}#{i}: {t.Msg}");
            }
        }
    }

    public static void SetCondition(Game g, string slotId, int c) => g.WorkshopVehicle()!.Slots[slotId].Part!.Condition = c;
}
