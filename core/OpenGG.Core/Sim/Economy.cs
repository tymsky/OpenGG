// Money formulas and engine diagnosis.
//
// Part prices were measured in the original (private notes, 2026-09-25) and fit every sample to the cent:
//   condition value  q = colour / 3 × (1 + extra), extra random in about ±0.1; black is 0
//   JunkYard price   = min + (max − min) × 2/3 × q      (also what SCRAP pays; black shelf stock turns red as the
//                                                        yard is gone to, save what comes with that visit)
//   repair           = (max − min) × (1 − q) / 2        (the part turns green, the extra stays)
//   Catalog price    = max
// Money is paid and received to the cent, as the prices are shown. (The 2002 release, as it runs on
// today's Windows, drops the cents of every charge and of your cash on payday; we take that for a
// compatibility bug and don't copy it. See docs/FIDELITY.md.)

using OpenGG.Core.Content;

namespace OpenGG.Core.Sim;

public enum EngineOutcome { Dead, NoCrank, NoStart, Rough, Loud, Runs }

public sealed record Diagnosis(EngineOutcome Outcome, string Message, string? Sound);

public static class Economy
{
    /// <summary>Rounds to cents, for showing an amount.</summary>
    public static decimal Cents(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    public static decimal Round5(decimal v) => Math.Round(v / 5m, MidpointRounding.AwayFromZero) * 5m;
    /// <summary>What a price takes from your money: the price as shown, to the cent.</summary>
    public static decimal Charge(decimal price) => Cents(Math.Max(0, price));
    static decimal D(double v) => (decimal)v;

    public static bool CanRepair(PartInstance p) => p.Condition is Condition.Red or Condition.Yellow;

    /// <summary>Condition value: 1/3 for red, 2/3 yellow, 1 green, each shifted by the part's extra. A black
    /// part is worth nothing above its minimum price (measured: SCRAP offers the minimum for black parts).</summary>
    public static double Quality(PartInstance p) => Math.Max(p.Condition, Condition.Black) / 3.0 * (1 + p.Extra);

    /// <summary>The cheapest a part gets (the original's cost range starts here).</summary>
    public static decimal MinPrice(PartDef d) => Math.Min(d.PriceMin ?? 0, d.Price);

    /// <summary>What the JunkYard asks for this part, and what it pays when you scrap it.</summary>
    public static decimal PartValue(PartDef d, PartInstance p)
    {
        var min = MinPrice(d);
        return Math.Max(0, min + (d.Price - min) * 2 / 3 * D(Quality(p)));
    }

    public static decimal PartValue(ContentIndex ci, PartInstance p) => PartValue(ci.Part(p.PartId), p);

    public static decimal RepairCost(ContentIndex ci, PartInstance p)
    {
        if (!CanRepair(p)) return 0;
        var d = ci.Part(p.PartId);
        return Math.Max(0, (d.Price - MinPrice(d)) * D(1 - Quality(p)) / 2);
    }

    public static decimal ScrapValue(ContentIndex ci, PartInstance p) => PartValue(ci, p);

    /// <summary>What the JunkYard asks: the part's value, a black one's its least (measured: a Starter that came black with
    /// the visit, $10.00). Black stock older than the visit is red by then (see <see cref="Game.EnterJunkyard"/>).</summary>
    public static decimal JunkPrice(ContentIndex ci, PartInstance p) => PartValue(ci, p);

    /// <summary>What it takes to put these places of a car right: the repair cost of each damaged part, the
    /// catalog price of each black or missing one.</summary>
    public static decimal FixCost(ContentIndex ci, VehicleState v, IEnumerable<string> slotIds)
    {
        decimal sum = 0;
        foreach (var id in slotIds)
        {
            if (v.Slots[id].Part is not { } p)
            {
                if (ci.Slot(v.ModelId, id).DefaultPart is { } dp) sum += ci.Part(dp).Price;
            }
            else if (p.Condition == Condition.Black) sum += ci.Part(p.PartId).Price;
            else sum += RepairCost(ci, p);
        }
        return sum;
    }

    /// <summary>A random job's fee is this many times <see cref="FixCost"/> (measured: $125.00 for a black
    /// $10–100 steering wheel, and single yellow or red parts within their random extra).</summary>
    public const decimal RandomJobFeeFactor = 1.25m;

    /// <summary>The Job Request's difficulty word, from the fee.</summary>
    public static Difficulty DifficultyOf(RulesDef r, decimal fee)
    {
        int i = 0;
        while (i < r.DifficultyFees.Length && fee >= r.DifficultyFees[i]) i++;
        return (Difficulty)Math.Min(i, (int)Difficulty.Expert);
    }

    /// <summary>
    /// The original's job packs can price a job "AUTO": a percentage (100, 110 or 120 % seen) of the catalog
    /// prices of the parts the job is about. Phrased random jobs are priced that way at 110 % (our guess).
    /// </summary>
    public static decimal AutoFee(ContentIndex ci, string carId, IEnumerable<string> slotIds, double percent = 110) =>
        Cents(slotIds.Select(id => ci.Slot(carId, id).DefaultPart).Where(p => p is not null).Sum(p => ci.Part(p!).Price) * D(percent / 100));

    /// <summary>The fee a customer offers. It is also the job's budget for parts and repairs.</summary>
    public static decimal JobFee(ContentIndex ci, string carId, IReadOnlyList<JobReq> reqs, Difficulty difficulty, JobTemplateDef tpl)
    {
        var r = ci.Pack.Rules;
        decimal parts = 0;
        var slots = new List<string>();
        foreach (var req in reqs)
            foreach (var id in req.SlotIds)
            {
                slots.Add(id);
                var s = ci.Slot(carId, id);
                decimal price = s.DefaultPart is { } dp ? ci.Part(dp).Price : 0;
                if (req.Parts is { Count: > 0 })
                {
                    var fits = req.Parts.Where(p => ci.HasPart(p) && ci.Part(p).SlotType == s.SlotType).Select(p => ci.Part(p).Price).ToList();
                    if (fits.Count > 0) price = fits.Min();
                }
                parts += price * D(r.FeePartFactor);
            }
        decimal labor = 0;
        foreach (var id in RemovalClosure(ci, carId, slots))
        {
            var s = ci.Slot(carId, id);
            var def = s.DefaultPart is { } dp ? ci.Part(dp) : null;
            labor += r.LaborPerPart + (def is null ? 0 : def.Fasteners.Count * r.LaborPerFastener);
        }
        return Math.Max(10, Round5((parts + labor) * D(r.DifficultyFactor(difficulty)) * D(tpl.FeeMultiplier)));
    }

    static HashSet<string> RemovalClosure(ContentIndex ci, string carId, List<string> slots) =>
        VehicleRules.RemovalClosure(ci, carId, slots);

    /// <summary>
    /// The Auction's step for a car as it is, whoever's it is (measured, sessions 14 and 15: cars of known make-up put on
    /// the block in 77 set-ups, and bought ones; the same car twice alike, its Orig and Repair Cost no matter). A base of
    /// 1.096 % of the catalog prices of all the model's parts, stock and custom (a lone black part shows it: seven models).
    /// On top, each part on the car at its catalog price times the colour of the car's average condition, times 5.494 %
    /// in a region with all its stock parts or 4.503 % in one not whole; the latter times n / (n − 1) for a car of n parts,
    /// so a lone part adds nothing and a pair twice its share; a custom part adds 2.23 % more of its price (alone too).
    /// The average is the parts' condition values (black 0 … green 1, each shifted by its extra), black ones included; its
    /// colour weighs 0.155 at red, 0.472 at yellow and 1 at green, straight lines between and 1.19 a unit past green. In
    /// whole $5 rounded down (a Car03's base: 25), never under the base. An empirical fit: all 80 steps known within $5,
    /// 70 of them exact.
    /// </summary>
    public static decimal AuctionStep(ContentIndex ci, VehicleState v)
    {
        var car = ci.Car(v.ModelId);
        var types = car.Slots.Select(s => s.SlotType).ToHashSet();
        double b = (double)ci.Pack.Parts.Where(p => types.Contains(p.SlotType)).Sum(p => p.Price) * 0.01096;
        var on = car.Slots.Where(s => v.Slots.TryGetValue(s.Id, out var st) && st.Part is not null).Select(s => (Slot: s, Part: v.Slots[s.Id].Part!)).ToList();
        if (on.Count == 0) return Floor5(b);
        var whole = car.Slots.Where(s => s.DefaultPart is not null).GroupBy(s => s.Region)
            .Where(r => r.All(s => v.Slots.TryGetValue(s.Id, out var st) && st.Part is not null)).Select(r => r.Key).ToHashSet();
        double colour = StepColour(on.Average(x => Quality(x.Part)));
        double inWhole = 0, notWhole = 0, custom = 0;
        foreach (var (slot, part) in on)
        {
            var d = ci.Part(part.PartId);
            double price = (double)d.Price * colour;
            if (whole.Contains(slot.Region)) inWhole += price;
            else notWhole += price;
            if (d.Custom) custom += price;
        }
        int n = on.Count;
        double more = inWhole * 0.05494 + (n > 1 ? notWhole * 0.04503 * n / (n - 1) : 0) + custom * 0.0223;
        return Math.Max(Floor5(b), Floor5(b + more));
    }

    /// <summary>The step's weight for a car's average condition value (see <see cref="AuctionStep"/>).</summary>
    static double StepColour(double q) => q switch
    {
        <= 0 => 0,
        < 1 / 3.0 => q * 3 * 0.1546,
        < 2 / 3.0 => 0.1546 + (q - 1 / 3.0) * 3 * (0.4722 - 0.1546),
        < 1 => 0.4722 + (q - 2 / 3.0) * 3 * (1 - 0.4722),
        _ => 1 + (q - 1) * 1.189,
    };

    static decimal Floor5(double v) => (decimal)(Math.Floor(v / 5 + 1e-9) * 5);

    /// <summary>What a car is worth to auction bidders: what its parts would fetch at the JunkYard.
    /// (One measured sale: bidders stopped at 85% of this.)</summary>
    public static decimal CarValue(ContentIndex ci, VehicleState v) =>
        v.Slots.Values.Where(s => s.Part is not null).Sum(s => PartValue(ci, s.Part!));

    /// <summary>Average condition of the required parts (missing counts as 0).</summary>
    public static double AverageCondition(ContentIndex ci, VehicleState v)
    {
        double sum = 0;
        int n = 0;
        foreach (var s in ci.Car(v.ModelId).Slots)
        {
            if (!s.Required) continue;
            var st = v.Slots[s.Id];
            sum += st.Part?.Condition ?? 0;
            n++;
        }
        return n > 0 ? sum / n : 0;
    }

    public static string ConditionLabel(double avg) => avg switch
    {
        >= 2.8 => "excellent",
        >= 2.3 => "good",
        >= 1.7 => "fair",
        >= 1.1 => "poor",
        _ => "a wreck",
    };

    // ---- Start Engine -------------------------------------------------------------------

    public static Diagnosis Diagnose(ContentIndex ci, VehicleState v)
    {
        var d = ci.Pack.Rules.Diagnosis;
        var car = ci.Car(v.ModelId);
        // A part off the car, or on it with a bolt out, is missed only where the car needs one (an accessory's empty
        // place stops nothing: measured, a Fairmont without its custom booster started).
        bool Gone(SlotDef s) => v.Slots[s.Id] is var st && (st.Part is null || st.Fasteners.Any(f => !f));
        // The engine's own troubles are looked for in the engine only (a family is a word in a part's name: the
        // original's "Headlights" would pass for a cylinder head); the exhaust's anywhere.
        bool Broken(List<string> families, int minOk, bool engine = true) =>
            car.Slots.Any(s => families.Contains(s.Family) && (!engine || s.Region == Region.Engine)
                && (Gone(s) ? s.Required : v.Slots[s.Id].Part!.Condition < minOk));
        bool Damaged(int below, bool block = false) => car.Slots.Any(s => s.Region == Region.Engine && !Gone(s) && v.Slots[s.Id].Part is { } p
            && p.Condition < below && ci.Part(p.PartId).Block == block);
        int hard = d.WorkingCondition;
        int soft = Math.Min(Condition.Green, hard + 1);
        if (Broken(d.Dead, hard)) return new(EngineOutcome.Dead, "Nothing happens. Not even a click.", null);
        if (Broken(d.NoCrank, hard) || d.EngineClicksBelow is { } below && Damaged(below))
            return new(EngineOutcome.NoCrank, "Click... click. The starter won't turn the engine over.", "snd.engine_click");
        if (Broken(d.NoStart, hard) || d.BlockCranksBelow is { } worn && Damaged(worn, block: true))
            return new(EngineOutcome.NoStart, "The engine cranks and cranks, but it won't start.", "snd.engine_crank");
        if (Broken(d.Rough, soft)) return new(EngineOutcome.Rough, "It starts, but it shakes, misfires and knocks.", "snd.engine_rough");
        if (Broken(d.Loud, soft, engine: false)) return new(EngineOutcome.Loud, "It runs, but it's LOUD. Something is wrong with the exhaust.", "snd.engine_loud");
        return new(EngineOutcome.Runs, "It purrs like a kitten.", "snd.engine_start");
    }
}
