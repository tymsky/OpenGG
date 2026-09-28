// Importing the original's mechanic saves (Data/Mechanics/*.mek) into OpenGG games.

using OpenGG.Core.Content;
using OpenGG.Core.Sim;

namespace OpenGG.Core.Original;

/// <summary>
/// Turns a mechanic of the original (a .mek save) into an OpenGG game on the original's content: name,
/// cash, skill, play time, jobs done, decals, the cars you own (parts, conditions, paint, costs), the
/// parts in the bins and the JunkYard shelves. Anything without a counterpart is reported, not guessed.
/// </summary>
public static class MekImport
{
    public sealed record Report(Game Game, List<string> Problems);

    /// <summary>Measured: a finished random job is saved in the jobs-done bits as number 2047.</summary>
    public const int RandomJobNumber = 2047;

    public static Report Import(OriginalGame og, ContentIndex ci, OrigMechanic m, uint? seed = null)
    {
        var problems = new List<string>();
        var g = Game.Create(ci, m.Name.Trim().Length > 0 ? m.Name.Trim() : "Mechanic", seed);
        var s = g.State;
        string NewId(string prefix) => $"{prefix}{s.NextId++}";

        // The save names cars by the number in their .car file; several files can share one (fan cars):
        // the first by file name wins.
        string? CarId(int number)
        {
            if (og.CarsByNumber.TryGetValue(number, out var ids) && ids.Find(ci.HasCar) is { } id) return id;
            problems.Add($"no car with number {number}");
            return null;
        }
        PartInstance? Part(MekItem it, int fallbackCar)
        {
            if (CarId(it.Car != 0 ? it.Car : fallbackCar) is not { } carId) return null;
            var pid = OriginalGame.PartId(carId, it.Part);
            if (!ci.HasPart(pid))
            {
                problems.Add($"{ci.Car(carId).Name}: no part number {it.Part}");
                return null;
            }
            return new PartInstance { Uid = NewId("p"), PartId = pid, Condition = Math.Clamp(it.Condition, Condition.Black, Condition.Green), Extra = it.Extra };
        }

        s.Cash = Cents(m.Cash);
        s.BestCash = Math.Max(s.Cash, ci.Pack.Rules.StartCash);
        s.Skill = Math.Clamp(m.Skill, 0, Math.Max(0, ci.Pack.Rules.Skills.Count - 1));
        s.PlayTime = m.PlayTime;

        // Jobs done: the save keeps one bit per job number, the job packs number their jobs.
        var jobsByNumber = new Dictionary<int, string>();
        foreach (var j in ci.Pack.Jobs)
            if (j.Id.StartsWith("jpk.", StringComparison.Ordinal) && int.TryParse(j.Id[(j.Id.LastIndexOf('.') + 1)..], out int n))
                jobsByNumber.TryAdd(n, j.Id);
        foreach (int n in m.JobsDone)
            if (jobsByNumber.TryGetValue(n, out var id)) s.CompletedJobs.Add(id);
            else if (n != RandomJobNumber) problems.Add($"job number {n} is in no job pack");
        s.FreePlay = g.JobChain.Any() && g.JobChain.All(j => s.CompletedJobs.Contains(j.Id));
        s.Stats.JobsDone = s.CompletedJobs.Count;

        // Decals: by their number in the decal packs.
        foreach (var d in m.Decals)
        {
            var def = ci.Pack.Decals.Find(x => x.Id.StartsWith("dpk.", StringComparison.Ordinal) && x.Id.EndsWith($".{d.Id}", StringComparison.Ordinal));
            if (def is null) problems.Add($"no decal number {d.Id}");
            else if (d.Uses > 0) s.DecalUses[def.Id] = d.Uses;
        }

        // The cars you own: the one in the WorkShop (the save names it by its Number), the others in the Car Lot in
        // their Numbers' order.
        s.Stats.CarsBought = Math.Max(m.CarsBought, m.Cars.Count > 0 ? m.Cars.Max(c => c.Number) + 1 : 0);
        foreach (var c in m.Cars.OrderBy(c => c.Number))
        {
            if (CarId(c.Car) is not { } carId) continue;
            var car = ci.Car(carId);
            var v = new VehicleState { Id = NewId("v"), ModelId = carId, Owner = Owner.Player, Paint = car.DefaultPaint, Number = c.Number };
            var items = c.Parts.ToList();
            foreach (var slot in car.Slots)
            {
                int k = items.FindIndex(it => Fits(it, slot));
                if (k < 0)
                {
                    v.Slots[slot.Id] = VehicleRules.EmptySlot();
                    continue;
                }
                var it = items[k];
                items.RemoveAt(k);
                v.Slots[slot.Id] = Part(it, c.Car) is { } p ? VehicleRules.FilledSlot(ci, p, tight: true) : VehicleRules.EmptySlot();
            }
            foreach (var it in items) problems.Add($"{car.Name}: part number {it.Part} has no place");
            v.Stats = new VehicleStats { OrigCost = Cents(c.Cost), RepairCost = Cents(c.RepairCost), RepairTime = c.RepairTimeMs / 1000.0 };
            v.Completed = c.Field1501 != 0;
            if (c.Paint is { } img) v.PaintImage = PaintOf(img).Encode();
            s.Vehicles[v.Id] = v;
            if (c.Number == m.WorkshopNumber && s.Workshop is null) s.Workshop = v.Id;
            else s.Lot.Add(v.Id);
            bool Fits(MekItem it, SlotDef slot) =>
                CarId(it.Car != 0 ? it.Car : c.Car) is { } cid && ci.HasPart(OriginalGame.PartId(cid, it.Part)) && ci.Part(OriginalGame.PartId(cid, it.Part)).SlotType == slot.SlotType;
        }

        // The Parts Bin (the save keeps one per car model) and the JunkYard shelves.
        foreach (var grp in m.Groups)
        {
            foreach (var it in grp.Bin)
                if (Part(it, grp.Car) is { } p) s.Bin.Add(new BinItem { Part = p, From = "catalog" });
            if (grp.Shelf.Count == 0 || CarId(grp.Car) is not { } shelfCar) continue;
            var shelf = new List<JunkItem>();
            // A black part in a saved shelf came with the last visit to the yard; it turns red as the yard is next gone to
            // (Game.EnterJunkyard), as in the original.
            foreach (var it in grp.Shelf)
                if (Part(it, grp.Car) is { } p)
                    shelf.Add(new JunkItem { Id = NewId("j"), Part = p });
            s.Shelves[shelfCar] = shelf;
        }
        s.Log.Add(new LogEntry { Text = $"Brought over from the original's save ({m.Name}).", Kind = LogKind.Info });
        return new Report(g, problems.Distinct().ToList());
    }

    static decimal Cents(float v) => Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero);

    /// <summary>The save's paint picture, as our 256 x 256 paint picture.</summary>
    static PaintCanvas PaintOf(OrigImage img)
    {
        var c = new PaintCanvas();
        int w = Math.Max(1, img.Width), h = Math.Max(1, img.Height);
        for (int y = 0; y < PaintCanvas.Size; y++)
            for (int x = 0; x < PaintCanvas.Size; x++)
            {
                int sx = x * w / PaintCanvas.Size, sy = y * h / PaintCanvas.Size;
                int si = (sy * w + sx) * 4, di = (y * PaintCanvas.Size + x) * 3;
                c.Pixels[di] = img.Rgba[si];
                c.Pixels[di + 1] = img.Rgba[si + 1];
                c.Pixels[di + 2] = img.Rgba[si + 2];
            }
        return c;
    }
}
