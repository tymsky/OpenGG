// The game: state + commands. The renderer and UI only read state and call commands.
// Flow follows the original (see docs/FIDELITY.md):
//   Get A Job -> Job Request (OK/CANCEL) -> fix the car, paying parts from the job's fee
//   -> the job completes by itself when the car is assembled and the request is met.
//   Between jobs: buy wrecks at the Auction, fix them, park them in the Car Lot, auction them off.

using System.Globalization;
using OpenGG.Core.Content;
using static OpenGG.Core.Sim.Economy;

namespace OpenGG.Core.Sim;

public sealed class Game
{
    public const int SaveVersion = 3;

    static readonly string[] DefaultNags =
    [
        "Is my {car} ready yet? I need it for work tomorrow.",
        "How much longer? I have been waiting all day!",
        "Please be careful with my {car}. It is my baby.",
    ];

    static readonly string[] DefaultThanks =
    [
        "Wow, it runs like new! Thanks a million.",
        "You saved my life. I will tell all my friends about you!",
        "Perfect. Here, keep the change.",
    ];

    public ContentIndex CI { get; }
    public GameState State { get; }
    public Rng Rng { get; }

    /// <summary>Raised for every change; see <see cref="GameEvent"/>.</summary>
    public event Action<GameEvent>? Event;

    public Game(ContentIndex ci, GameState state)
    {
        CI = ci;
        State = state;
        Rng = new Rng(state);
    }

    public static Game Create(ContentIndex ci, string mechanic = "Mechanic", uint? seed = null)
    {
        var r = ci.Pack.Rules;
        uint s = seed ?? (uint)Environment.TickCount64;
        var state = new GameState
        {
            ContentId = ci.Pack.Id,
            Seed = s,
            Rng = s,
            Mechanic = mechanic,
            Cash = r.StartCash,
            BestCash = r.StartCash,
        };
        return new Game(ci, state);
    }

    public static Game Load(ContentIndex ci, string json)
    {
        var state = Json.Parse<GameState>(json);
        if (state.Version != SaveVersion) throw new InvalidDataException($"Unsupported save version {state.Version}.");
        if (state.ContentId != ci.Pack.Id) throw new InvalidDataException($"The save was made with content \"{state.ContentId}\", current is \"{ci.Pack.Id}\".");
        var g = new Game(ci, state);
        g.FitToContent();
        g.NumberCars();
        return g;
    }

    /// <summary>What <see cref="Load"/> had to leave out because the content no longer has it (cars, parts, places on
    /// a car, decals, shelves).</summary>
    public List<string> Problems { get; } = [];

    /// <summary>
    /// A save made with other content than the one loaded now (the player's copy of the original changed: a car file
    /// taken away, one made over, another copy): whatever it names that is not there any more is left out, and places
    /// a car has gained are empty, so nothing asks the content for what it lacks.
    /// </summary>
    void FitToContent()
    {
        var s = State;
        foreach (var (id, v) in s.Vehicles.ToList())
        {
            if (CI.HasCar(v.ModelId)) FitVehicle(v);
            else
            {
                s.Vehicles.Remove(id);
                Problems.Add($"car {v.ModelId}: not in the content, left out");
            }
        }
        s.Lot.RemoveAll(id => !s.Vehicles.ContainsKey(id));
        if (s.Workshop is { } w && !s.Vehicles.ContainsKey(w)) s.Workshop = null;
        if (s.Auction is { } a && !s.Vehicles.ContainsKey(a.VehicleId)) s.Auction = null;
        if (s.JunkFor is { } jf && !CI.HasCar(jf)) s.JunkFor = null;
        foreach (var job in new[] { s.Job, s.Offer })
        {
            if (job is null) continue;
            if (!s.Vehicles.TryGetValue(job.VehicleId, out var jv))
            {
                if (job == s.Job) s.Job = null;
                else s.Offer = null;
                Problems.Add($"job {job.TemplateId}: its car is gone, the job with it");
                continue;
            }
            foreach (var rq in job.Reqs)
            {
                rq.SlotIds.RemoveAll(id => !CI.HasSlot(jv.ModelId, id));
                rq.Parts?.RemoveAll(p => !CI.HasPart(p));
            }
            job.Reqs.RemoveAll(rq => rq.SlotIds.Count == 0);
        }
        int bin = s.Bin.RemoveAll(b => !CI.HasPart(b.Part.PartId));
        foreach (var it in s.PurchaseBin.Where(it => !CI.HasPart(it.Part.PartId))) s.Cash += it.Paid;
        bin += s.PurchaseBin.RemoveAll(it => !CI.HasPart(it.Part.PartId));
        if (bin > 0) Problems.Add($"{bin} part(s) in the bins: not in the content, left out");
        foreach (var (model, shelf) in s.Shelves.ToList())
        {
            if (!CI.HasCar(model)) s.Shelves.Remove(model);
            else shelf.RemoveAll(it => !CI.HasPart(it.Part.PartId));
        }
        foreach (var d in s.DecalUses.Keys.ToList())
            if (CI.Pack.Decals.Find(x => x.Id == d) is null) s.DecalUses.Remove(d);
    }

    /// <summary>One car made to fit its model as the content has it now: places it no longer has give their parts to
    /// the Parts Bin (yours) or lose them; a part the content lacks, or one of another kind than its place, comes off;
    /// a place it has gained is empty; bolts that do not match the part are put in.</summary>
    void FitVehicle(VehicleState v)
    {
        var car = CI.Car(v.ModelId);
        foreach (var (slotId, st) in v.Slots.ToList())
        {
            bool known = CI.HasSlot(v.ModelId, slotId);
            if (st.Part is { } p && (!known || !CI.TryPart(p.PartId, out var def) || def.SlotType != CI.Slot(v.ModelId, slotId).SlotType))
            {
                if (CI.HasPart(p.PartId) && v.Owner == Owner.Player) State.Bin.Add(new BinItem { Part = p, From = v.Id });
                Problems.Add($"car {v.ModelId}: {p.PartId} in {slotId} does not fit the content, taken off");
                st.Part = null;
                st.Fasteners = [];
                st.Open = false;
            }
            if (!known) v.Slots.Remove(slotId);
        }
        foreach (var slot in car.Slots)
        {
            if (!v.Slots.TryGetValue(slot.Id, out var st)) v.Slots[slot.Id] = st = new SlotState();
            int bolts = st.Part is { } p ? CI.Part(p.PartId).Fasteners.Count : 0;
            if (st.Fasteners.Count != bolts) st.Fasteners = Enumerable.Repeat(st.Part is not null, bolts).ToList();
        }
        v.Decals.RemoveAll(d => CI.Pack.Decals.Find(x => x.Id == d.DecalId) is null);
    }

    /// <summary>Saves from before the Car Lot's Numbers: your cars get the next ones, in the order they are parked.</summary>
    void NumberCars()
    {
        var cars = State.Lot.Select(id => State.Vehicles.GetValueOrDefault(id)).Append(WorkshopVehicle() is { Owner: Owner.Player } w ? w : null);
        foreach (var v in cars.OfType<VehicleState>().Where(v => v.Number is null)) v.Number = State.Stats.CarsBought++;
    }

    public string Save()
    {
        FlushCanvases();
        return Json.Write(State);
    }

    public static string Money(decimal v) => "$" + v.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- events ------------------------------------------------------------------------

    void Emit(GameEvent e) => Event?.Invoke(e);

    void Changed() => Emit(new ChangedEvent());

    void Log(string text, LogKind kind = LogKind.Info, bool toast = true)
    {
        State.Log.Add(new LogEntry { Text = text, Kind = kind });
        if (State.Log.Count > 60) State.Log.RemoveAt(0);
        if (toast) Emit(new ToastEvent(text, kind));
    }

    string NewId(string prefix) => $"{prefix}{State.NextId++}";

    // ---- money -------------------------------------------------------------------------

    /// <summary>The account purchases come from right now: the job budget during a job, else cash.</summary>
    public Account Account => State.Job is null ? Account.Cash : Account.Job;

    /// <summary>Money available for purchases right now.</summary>
    public decimal Funds => State.Job?.Budget ?? State.Cash;

    /// <summary>Can the current account pay this price (to the cent, as it will be charged)?</summary>
    public bool CanAfford(decimal price) => Funds >= Charge(price);

    /// <summary>Pays a price from the job budget during a job, else from cash, to the cent.
    /// Returns what was taken.</summary>
    decimal Spend(decimal price)
    {
        var amount = Charge(price);
        if (State.Job is { } job) job.Budget -= amount;
        else
        {
            State.Cash -= amount;
            State.Stats.Spent += amount;
            if (WorkshopVehicle() is { Owner: Owner.Player } v) v.Stats.RepairCost += amount;
        }
        Emit(new MoneyEvent(-amount, Account));
        return amount;
    }

    /// <summary>Money coming in, as it was stated (a fee keeps all of its value).</summary>
    void Receive(decimal amount, Account? account = null)
    {
        var acc = account ?? Account;
        if (acc == Account.Job && State.Job is { } job) job.Budget += amount;
        else
        {
            State.Cash += amount;
            State.Stats.Earned += amount;
            UpdateSkill();
        }
        Emit(new MoneyEvent(amount, acc));
    }

    /// <summary>The skill levels your money has reached, as you sign in (measured: a save's cash set past two levels'
    /// bars brought both advances as the mechanic signed in).</summary>
    public void CheckSkill() => UpdateSkill();

    void UpdateSkill()
    {
        State.BestCash = Math.Max(State.BestCash, State.Cash);
        if (InJobsMode) return; // the original keeps you Learning until the tutorial jobs are done
        var skills = CI.Pack.Rules.Skills;
        int level = 0;
        for (int i = 0; i < skills.Count; i++)
            if (State.BestCash >= skills[i].Cash && State.Stats.CarsRepaired >= skills[i].Cars) level = i;
        if (JobChain.Any()) level = Math.Max(level, Math.Min(1, skills.Count - 1)); // Jobs Mode done: Novice
        SetSkill(level);
    }

    /// <summary>Measured: levels come one at a time, each with its own Skill Advance (Novice to Handy, then Handy to
    /// Expert, each followed by its Available Cars).</summary>
    void SetSkill(int level)
    {
        var skills = CI.Pack.Rules.Skills;
        while (State.Skill < level)
        {
            string from = SkillName;
            State.Skill++;
            var name = skills[State.Skill].Name;
            Emit(new SkillEvent(State.Skill, name, from, payingJob));
            Log($"Skill Advance! You just moved up from {from} to {name}.", LogKind.Good);
        }
    }

    public string SkillName => State.Skill < CI.Pack.Rules.Skills.Count ? CI.Pack.Rules.Skills[State.Skill].Name : "";

    // ---- queries -----------------------------------------------------------------------

    public VehicleState Vehicle(string id) =>
        State.Vehicles.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"unknown vehicle {id}");

    public CarModelDef Car(VehicleState v) => CI.Car(v.ModelId);

    public string CarName(VehicleState v) => CI.Car(v.ModelId).DisplayName;

    public VehicleState? WorkshopVehicle() =>
        State.Workshop is { } id && State.Vehicles.TryGetValue(id, out var v) ? v : null;

    public BinItem? Item(string uid) => State.Bin.Find(i => i.Part.Uid == uid);

    // ---- workshop: bolts and parts -----------------------------------------------------

    public Result<bool> UseFastener(string slotId, int index, string toolId)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<bool>("There's no car in your WorkShop.");
        var c = VehicleRules.FastenerCheck(CI, v, slotId, index, toolId);
        if (!c.Ok) return Result.From<bool>(c);
        var st = v.Slots[slotId];
        bool wasIn = st.Fasteners[index];
        st.Fasteners[index] = !wasIn;
        Emit(new FastenerEvent(v.Id, slotId, index, wasIn, toolId));
        AfterWork();
        return Result.Success(wasIn);
    }

    /// <summary>Undo (<paramref name="remove"/>) or tighten every bolt of a part that the tool fits.</summary>
    public Result<int> UseAllFasteners(string slotId, string toolId, bool remove)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<int>("There's no car in your WorkShop.");
        if (!v.Slots.TryGetValue(slotId, out var st) || st.Part is null) return Result.Fail<int>("Nothing installed there.");
        var def = CI.Part(st.Part.PartId);
        int count = 0;
        Result<bool>? last = null;
        for (int i = 0; i < def.Fasteners.Count; i++)
        {
            if (WorkshopVehicle() != v || v.Slots[slotId].Part is null) break; // the job finished
            if (v.Slots[slotId].Fasteners[i] != remove) continue;
            var r = UseFastener(slotId, i, toolId);
            last = r;
            if (r.Ok) count++;
            else if (r.Code != "wrong_tool") return Result.Fail<int>(r.Msg ?? "You can't do that.", r.Code, r.Slots);
        }
        if (count == 0 && last is { Ok: false } l) return Result.Fail<int>(l.Msg ?? "Wrong tool.", l.Code);
        return Result.Success(count);
    }

    public Result<string> RemovePart(string slotId)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<string>("There's no car in your WorkShop.");
        var c = VehicleRules.RemoveCheck(CI, v, slotId);
        if (!c.Ok) return Result.From<string>(c);
        var part = v.Slots[slotId].Part!;
        State.Bin.Add(new BinItem { Part = part, From = v.Id });
        v.Slots[slotId] = VehicleRules.EmptySlot();
        Emit(new PartEvent(v.Id, slotId, true));
        AfterWork();
        return Result.Success(part.Uid);
    }

    public Result<Unit> InstallPart(string slotId, string uid)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        int idx = State.Bin.FindIndex(i => i.Part.Uid == uid);
        if (idx < 0) return Result.Fail("That part is not in your Parts Bin.");
        var item = State.Bin[idx];
        if (VehicleRules.PartCheck(item.Part) is { Ok: false } worn) return Result.From<Unit>(worn);
        var c = VehicleRules.InstallCheck(CI, v, slotId, item.Part.PartId);
        if (!c.Ok) return Result.From<Unit>(c);
        State.Bin.RemoveAt(idx);
        v.Slots[slotId] = VehicleRules.FilledSlot(CI, item.Part, tight: false);
        if (CI.Slot(v.ModelId, slotId).Openable is not null) v.Slots[slotId].Open = true;
        Emit(new PartEvent(v.Id, slotId, false));
        AfterWork();
        return Result.Done();
    }

    /// <summary>
    /// A part put on but not yet bolted down goes back into the Parts Bin, at <paramref name="binIndex"/>. Measured:
    /// CANCEL in the Impact Wrench's box while its bolts are being done up takes the part off the car and back into
    /// the bin (the bin had kept showing it meanwhile).
    /// </summary>
    public Result<Unit> CancelAttach(string slotId, int binIndex, string from)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        if (!v.Slots.TryGetValue(slotId, out var st) || st.Part is not { } part) return Result.Fail("Nothing installed there.");
        if (VehicleRules.MountedChildren(CI, v, slotId) is { Count: > 0 } kids)
            return Result.Fail($"The {CI.Slot(v.ModelId, slotId).Name} holds other parts now.", "children", kids);
        v.Slots[slotId] = VehicleRules.EmptySlot();
        State.Bin.Insert(Math.Clamp(binIndex, 0, State.Bin.Count), new BinItem { Part = part, From = from });
        Emit(new PartEvent(v.Id, slotId, true, Undo: true));
        Changed();
        return Result.Done();
    }

    public Result<bool> ToggleOpen(string slotId)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<bool>("There's no car in your WorkShop.");
        var slot = CI.Slot(v.ModelId, slotId);
        var st = v.Slots[slotId];
        if (slot.Openable is null || st.Part is null) return Result.Fail<bool>($"The {slot.Name} doesn't open.");
        if (st.Open && st.Fasteners.Any(f => !f)) return Result.Fail<bool>($"Bolt the {slot.Name} back on before closing it.");
        st.Open = !st.Open;
        Emit(new OpenEvent(v.Id, slotId, st.Open));
        Changed();
        return Result.Success(st.Open);
    }

    public Result<Diagnosis> StartEngine()
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<Diagnosis>("There's no car in your WorkShop.");
        return Result.Success(Diagnose(CI, v));
    }

    void AfterWork()
    {
        CheckJob();
        CheckCarComplete();
        Changed();
    }

    /// <summary>
    /// Measured: the first time a car of yours in the WorkShop is in top condition (nothing missing, every bolt in, every
    /// part green; its accessories don't count), "Car Complete" comes up with the time it took and what it cost (its Repair
    /// Time and Repair Cost), at once: as its last part's last bolt goes in, or as it comes into the WorkShop so (signing
    /// in); its clock stops there. It comes once for a car (the original keeps it in the car's field 1501).
    /// </summary>
    public void CheckCarComplete()
    {
        if (State.Job is not null || WorkshopVehicle() is not { Owner: Owner.Player, Completed: false } v) return;
        if (VehicleRules.AssembledCondition(CI, v) != Condition.Green) return;
        v.Completed = true;
        Emit(new CarCompleteEvent(v.Id, v.Stats.RepairTime, v.Stats.RepairCost));
    }

    // ---- parts bin: repair and scrap ---------------------------------------------------

    public Result<decimal> RepairItem(string uid)
    {
        if (Item(uid) is not { } it) return Result.Fail<decimal>("No such part.");
        var def = CI.Part(it.Part.PartId);
        if (it.Part.Condition == Condition.Black)
            return Result.Fail<decimal>($"This {def.Name} is too damaged to repair. It can only be scrapped for a few dollars at the JunkYard.", "broken");
        if (!CanRepair(it.Part)) return Result.Fail<decimal>($"The {def.Name} is already in good shape.", "good");
        var cost = RepairCost(CI, it.Part);
        if (!CanAfford(cost)) return Result.Fail<decimal>($"Fixing up the {def.Name} costs {Money(cost)}. {NotEnough}", "cash");
        var paid = Spend(cost);
        it.Part.Condition = Condition.Good; // the extra stays: a repaired part is worth what a green one with it is
        Changed();
        return Result.Success(paid);
    }

    public Result<decimal> ScrapItem(string uid)
    {
        int idx = State.Bin.FindIndex(i => i.Part.Uid == uid);
        if (idx < 0) return Result.Fail<decimal>("No such part.");
        var it = State.Bin[idx];
        var value = Cents(ScrapValue(CI, it.Part)); // what the Scrap Part dialog offers
        State.Bin.RemoveAt(idx);
        if (value > 0) Receive(value);
        // Scrapped parts turn up on the JunkYard shelf of their car. A destroyed (black) part comes back as red.
        var back = it.Part.Clone();
        if (back.Condition == Condition.Black) back.Condition = Condition.Red;
        back.Color = null;
        if (CarOfPart(back.PartId) is { } model) Shelf(model).Add(new JunkItem { Id = NewId("j"), Part = back });
        Changed();
        return Result.Success(value);
    }

    // ---- catalog and junkyard ----------------------------------------------------------

    /// <summary>A random extra for a new part. Measured: new Catalog parts came out at q ≈ 0.91–1.07, the same
    /// random extra as any other part.</summary>
    double NewPartExtra() => Rng.Range(-CI.Pack.Rules.ConditionSpread, CI.Pack.Rules.ConditionSpread);

    double RandomExtra() => Rng.Range(-CI.Pack.Rules.ConditionSpread, CI.Pack.Rules.ConditionSpread);

    /// <summary>Catalog: a new part at the top of its price range.</summary>
    public Result<string> BuyPart(string partId)
    {
        var def = CI.Part(partId);
        if (!CanAfford(def.Price))
            return Result.Fail<string>($"A new {def.Name} costs {Money(def.Price)}. {NotEnough}", "cash");
        Spend(def.Price);
        var part = new PartInstance { Uid = NewId("p"), PartId = partId, Condition = Condition.Good, Extra = NewPartExtra() };
        State.Bin.Add(new BinItem { Part = part, From = "catalog" });
        Changed();
        return Result.Success(part.Uid);
    }

    /// <summary>Measured: the original keeps a Parts Bin per car model, so the WorkShop shows only the parts
    /// that fit the car in it (the others wait for their model), and none while the WorkShop is empty.</summary>
    public bool FitsCar(PartInstance p, string? modelId)
    {
        if (modelId is null) return false;
        if (!CI.HasCar(modelId)) return true;
        var type = CI.Part(p.PartId).SlotType;
        return CI.Car(modelId).Slots.Any(s => s.SlotType == type);
    }

    /// <summary>The Parts Bin as the WorkShop shows it: the parts that fit the car in it.</summary>
    public IEnumerable<BinItem> BinFor(string? modelId) => State.Bin.Where(b => FitsCar(b.Part, modelId));

    /// <summary>The car model a part belongs to (the original keeps one JunkYard shelf per model).</summary>
    string? CarOfPart(string partId)
    {
        var type = CI.Part(partId).SlotType;
        if (State.JunkFor is { } cur && CI.HasCar(cur) && CI.Car(cur).Slots.Any(s => s.SlotType == type)) return cur;
        return CI.Pack.Cars.Find(c => c.Slots.Any(s => s.SlotType == type))?.Id;
    }

    /// <summary>The JunkYard shelf of a car model; made now if it has none yet (a car from an old save, or none in the
    /// WorkShop).</summary>
    public List<JunkItem> Shelf(string carId) =>
        State.Shelves.TryGetValue(carId, out var shelf) ? shelf
            : NewShelf(carId, WorkshopVehicle() is { } v && v.ModelId == carId ? v : null, won: false);

    /// <summary>
    /// A model's JunkYard shelf, made as its first car comes in (a job taken, a car won at the Auction). Measured on the
    /// original's new shelves (22 in its saves): first one of each custom part the car has not, with what goes on them
    /// (the Checker Cab's whole Hemi kit, but not the engine its owner had taken out; never an accessory, the taxi sign),
    /// for a car won at the Auction also one of each stock part it came without (47 of 47 for a Monorail), all in the
    /// order of the car's file; then 15 to 21 of the model's parts at random, accessories aside; each in any colour. A car
    /// won whose model has its shelf already adds nothing to it (an Xa7).
    /// </summary>
    List<JunkItem> NewShelf(string carId, VehicleState? v, bool won)
    {
        var shelf = State.Shelves[carId] = [];
        if (!won && State.Job is { } job) (job.NewShelves ??= []).Add(carId);
        if (!CI.HasCar(carId)) return shelf;
        var car = CI.Car(carId);
        var parts = ShelfParts(car);
        if (parts.Count == 0) return shelf;
        var on = v?.Slots.Values.Where(s => s.Part is not null).Select(s => s.Part!.PartId).ToHashSet() ?? [];
        var gone = won && v is not null
            ? car.Slots.Where(s => s.DefaultPart is not null && v.Slots.GetValueOrDefault(s.Id)?.Part is null).Select(s => s.DefaultPart!).ToHashSet()
            : [];
        foreach (var p in parts)
            if ((p.Custom || p.AddOn) && !on.Contains(p.Id) || gone.Contains(p.Id)) shelf.Add(NewJunkItem(p));
        var more = CI.Pack.Rules.JunkShelfRandom;
        for (int i = 0, n = Rng.Int(more[0], more[^1]); i < n; i++) shelf.Add(NewJunkItem(Rng.Pick(parts)));
        return shelf;
    }

    /// <summary>What a model's shelf can hold: its parts in the order of its file, accessories aside (none was ever on
    /// a shelf).</summary>
    List<PartDef> ShelfParts(CarModelDef car) =>
        CI.Pack.Parts.Where(p => p.SoundKind != PartSound.Accessory && car.Slots.Any(s => s.SlotType == p.SlotType)).ToList();

    /// <summary>Stock for a shelf, in any colour. A new shelf's black stock is red by the time the yard shows it (see
    /// <see cref="EnterJunkyard"/>), so it is stocked as red; what comes as the yard is gone to can be black.</summary>
    JunkItem NewJunkItem(PartDef def, bool mayBeBlack = false)
    {
        int cond = Rng.Weighted(Condition.All, c => CI.Pack.Rules.JunkConditionWeights[c]);
        if (!mayBeBlack) cond = Math.Max(Condition.Red, cond);
        return new JunkItem { Id = NewId("j"), Part = new PartInstance { Uid = NewId("p"), PartId = def.Id, Condition = cond, Extra = RandomExtra() } };
    }

    /// <summary>
    /// Going to the JunkYard. Measured on 28 visits to a Wynn's shelf, from the save the original writes at each: before
    /// the yard comes up, the shelf of the car shown changes by itself. Its black parts turn red (all 12 that stayed), 0
    /// to 3 of its parts go (any of them), and 0 to 3 new ones come at its end, any of the model's parts in any colour.
    /// A new black one shows, and sells, at a black part's price until the next visit (a Starter at $10.00, its least).
    /// The other models' shelves stay as they are.
    /// </summary>
    public List<JunkItem> EnterJunkyard()
    {
        var shelf = OpenJunkyard();
        var r = CI.Pack.Rules;
        foreach (var it in shelf)
            if (it.Part.Condition == Condition.Black) it.Part.Condition = Condition.Red;
        int Count(double[] odds) => odds.Length == 0 ? 0 : Rng.Weighted(Enumerable.Range(0, odds.Length).ToList(), i => odds[i]);
        for (int i = 0, n = Count(r.JunkVisitGone); i < n && shelf.Count > 0; i++) shelf.RemoveAt(Rng.Int(0, shelf.Count - 1));
        var parts = State.JunkFor is { } carId && CI.HasCar(carId) ? ShelfParts(CI.Car(carId)) : [];
        for (int i = 0, n = Count(r.JunkVisitNew); i < n && parts.Count > 0; i++) shelf.Add(NewJunkItem(Rng.Pick(parts), mayBeBlack: true));
        Changed();
        return shelf;
    }

    /// <summary>What the JunkYard shows: the shelf of the car in the workshop (or of the last one).</summary>
    public List<JunkItem> Junk => OpenJunkyard();

    /// <summary>Going to the JunkYard: it shows the parts for the car in the workshop.</summary>
    public List<JunkItem> OpenJunkyard()
    {
        var model = WorkshopVehicle()?.ModelId ?? State.JunkFor;
        if (model is null || !CI.HasCar(model)) model = CI.Pack.Cars[0].Id;
        State.JunkFor = model;
        return Shelf(model);
    }

    /// <summary>After a job, <see cref="RulesDef.JunkChurn"/> shelf items go and as many come (none in the original).</summary>
    void ChurnShelf(string carId)
    {
        if (!State.Shelves.TryGetValue(carId, out var shelf)) return;
        var r = CI.Pack.Rules;
        int n = Rng.Int(r.JunkChurn[0], r.JunkChurn[1]);
        for (int i = 0; i < n && shelf.Count > 0; i++) shelf.RemoveAt(Rng.Int(0, shelf.Count - 1));
        var parts = CI.HasCar(carId) ? ShelfParts(CI.Car(carId)) : [];
        for (int i = 0; i < n && parts.Count > 0; i++) shelf.Add(NewJunkItem(Rng.Pick(parts)));
    }

    string NotEnough => Account == Account.Job ? "There isn't enough left in the job budget." : "You don't have enough cash.";

    /// <summary>JunkYard: click a part to buy it on the spot. It waits in the Purchase Bin until you leave.</summary>
    public Result<string> BuyJunk(string itemId)
    {
        var shelf = Junk;
        int idx = shelf.FindIndex(j => j.Id == itemId);
        if (idx < 0) return Result.Fail<string>("Somebody else bought that one.");
        var it = shelf[idx];
        var price = JunkPrice(CI, it.Part);
        if (!CanAfford(price)) return Result.Fail<string>(NotEnough, "cash");
        it.Paid = Spend(price);
        // A black part here came with this visit (the others turned red as the yard came up): it goes home as it is (not
        // seen).
        shelf.RemoveAt(idx);
        State.PurchaseBin.Add(it);
        Changed();
        return Result.Success(it.Part.Uid);
    }

    /// <summary>JunkYard: click a part in the Purchase Bin to give it back for what you paid.</summary>
    public Result<Unit> ReturnJunk(string itemId)
    {
        int idx = State.PurchaseBin.FindIndex(j => j.Id == itemId);
        if (idx < 0) return Result.Fail("That part is not in your Purchase Bin.");
        var it = State.PurchaseBin[idx];
        State.PurchaseBin.RemoveAt(idx);
        if (it.Paid > 0) Receive(it.Paid);
        it.Paid = 0;
        if (CarOfPart(it.Part.PartId) is { } model) Shelf(model).Add(it);
        Changed();
        return Result.Done();
    }

    /// <summary>Leaving the JunkYard: what you bought goes to the Parts Bin.</summary>
    public int LeaveJunkyard()
    {
        int n = State.PurchaseBin.Count;
        foreach (var it in State.PurchaseBin) State.Bin.Add(new BinItem { Part = it.Part, From = "junkyard" });
        State.PurchaseBin.Clear();
        if (n > 0) Changed();
        return n;
    }

    // ---- paint and decals --------------------------------------------------------------

    readonly Dictionary<string, (PaintCanvas Canvas, int Saved)> canvases = [];

    /// <summary>The paint picture of a car that has one (made from its colour the first time).</summary>
    public PaintCanvas? Canvas(VehicleState v)
    {
        if (!CI.HasCar(v.ModelId) || !CI.Car(v.ModelId).PaintAtlas) return null;
        if (canvases.TryGetValue(v.Id, out var c)) return c.Canvas;
        var canvas = PaintCanvas.Decode(v.PaintImage) ?? PaintCanvas.Filled(Rgb.FromHex(v.Paint));
        canvases[v.Id] = (canvas, canvas.Version);
        return canvas;
    }

    void FlushCanvases()
    {
        foreach (var (id, (canvas, saved)) in canvases.ToList())
        {
            if (!State.Vehicles.TryGetValue(id, out var v))
            {
                canvases.Remove(id);
                continue;
            }
            if (canvas.Version == saved && v.PaintImage is not null) continue;
            v.PaintImage = canvas.Encode();
            canvases[id] = (canvas, canvas.Version);
        }
    }

    /// <summary>
    /// A part in bare metal: one worse than green, which the original draws without paint. Measured: paint (the whole-part
    /// brush on a red, a yellow and a black panel) and a decal (on a red one) left no mark on it and nothing in the car's
    /// paint picture; the decal's use was gone all the same.
    /// </summary>
    bool Bare(VehicleState v, string? slotId) =>
        slotId is not null && v.Slots.TryGetValue(slotId, out var st) && st.Part is { Condition: < Condition.Green };

    /// <summary>Body Paint's panel brush on a car with a paint picture: fills the panel's square (the panel of
    /// <paramref name="slotId"/>, when it is a part's; nothing on a part in bare metal).</summary>
    public Result<Unit> PaintArea(double u0, double v0, double u1, double v1, string paintId, string? slotId = null)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        var paint = CI.Pack.Paints.Find(p => p.Id == paintId);
        if (paint is null) return Result.Fail("Unknown colour.");
        if (Canvas(v) is not { } canvas) return Result.Fail("This car can't be painted that way.");
        if (Bare(v, slotId)) return Result.Done();
        Spend(CI.Pack.Rules.PaintPrice);
        canvas.FillUv(u0, v0, u1, v1, Rgb.FromHex(paint.Color));
        Emit(new PaintEvent(v.Id));
        return Result.Done();
    }

    /// <summary>Body Paint's small, medium and large brushes: a square dab at a point of the paint picture,
    /// kept within that panel's square (nothing on a part in bare metal).</summary>
    public Result<Unit> Spray(double u, double v0, int side, string paintId, (double, double, double, double)? clip = null, string? slotId = null)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        var paint = CI.Pack.Paints.Find(p => p.Id == paintId);
        if (paint is null) return Result.Fail("Unknown colour.");
        if (Canvas(v) is not { } canvas) return Result.Fail("This car can't be painted that way.");
        if (Bare(v, slotId)) return Result.Done();
        canvas.Dab(u, v0, side, Rgb.FromHex(paint.Color), clip);
        Emit(new PaintEvent(v.Id));
        return Result.Done();
    }


    /// <summary>Big nozzle: recolour one part (a body panel).</summary>
    public Result<Unit> PaintPart(string slotId, string paintId)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        var paint = CI.Pack.Paints.Find(p => p.Id == paintId);
        if (paint is null) return Result.Fail("Unknown colour.");
        if (!v.Slots.TryGetValue(slotId, out var st) || st.Part is null) return Result.Fail("There is nothing to paint there.");
        if (CI.Slot(v.ModelId, slotId).Region != Region.Body) return Result.Fail("You can only paint the body.");
        if (Bare(v, slotId)) return Result.Done();
        Spend(CI.Pack.Rules.PaintPrice);
        st.Part.Color = paint.Color;
        Changed();
        return Result.Done();
    }

    /// <summary>Repaint the whole body in one colour.</summary>
    public Result<decimal> PaintCar(string paintId)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail<decimal>("There's no car in your WorkShop.");
        var paint = CI.Pack.Paints.Find(p => p.Id == paintId);
        if (paint is null) return Result.Fail<decimal>("Unknown colour.");
        var panels = Car(v).Slots.Where(s => s.Region == Region.Body && v.Slots[s.Id].Part is not null).ToList();
        var cost = CI.Pack.Rules.PaintPrice * (panels.Count + 2);
        Spend(cost);
        v.Paint = paint.Color;
        foreach (var s in panels) v.Slots[s.Id].Part!.Color = null;
        if (Canvas(v) is { } canvas) canvas.FillRect(0, 0, PaintCanvas.Size, PaintCanvas.Size, Rgb.FromHex(paint.Color));
        Changed();
        return Result.Success(cost);
    }

    public int DecalUses(string decalId) => State.DecalUses.GetValueOrDefault(decalId);

    /// <summary>Catalog: a decal comes with a fixed number of uses.</summary>
    public Result<Unit> BuyDecal(string decalId)
    {
        var d = CI.Pack.Decals.Find(x => x.Id == decalId);
        if (d is null) return Result.Fail("Unknown decal.");
        if (!CanAfford(d.Price)) return Result.Fail($"{Math.Max(1, d.Uses)} {d.Name} decals come to {Money(d.Price)}. {NotEnough}", "cash");
        Spend(d.Price);
        State.DecalUses[decalId] = DecalUses(decalId) + Math.Max(1, d.Uses);
        Changed();
        return Result.Done();
    }

    /// <summary>A decal stuck where the pointer is, on the panel of <paramref name="slotId"/> when it is a part's: one use
    /// is gone, and on a part in bare metal that is all (see <see cref="Bare"/>).</summary>
    public Result<Unit> AddDecal(string decalId, Vec3 pos, Vec3 normal, double angle, double size, bool flipX = false, bool flipY = false, string? color = null, string? slotId = null)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        var d = CI.Pack.Decals.Find(x => x.Id == decalId);
        if (d is null) return Result.Fail("Unknown decal.");
        if (DecalUses(decalId) < 1) return Result.Fail($"You are out of {d.Name} decals. Buy more in the Catalog.");
        State.DecalUses[decalId] = DecalUses(decalId) - 1;
        if (Bare(v, slotId))
        {
            Changed();
            return Result.Done();
        }
        v.Decals.Add(new DecalState { DecalId = decalId, Pos = pos, Normal = normal, Angle = angle, Size = size, FlipX = flipX, FlipY = flipY, Color = color });
        Changed();
        return Result.Done();
    }

    /// <summary>
    /// A decal on a car with a paint picture, the original's way (measured): the picture is laid flat on the screen where
    /// the pointer is and each of its pixels paints the pixel of the paint picture on the surface under it
    /// (<paramref name="texels"/>, worked out by the view), so it goes into the paint and paint put on later covers it;
    /// nothing on a part in bare metal. One use is gone wherever it lands, even where nothing takes it.
    /// </summary>
    public Result<Unit> StampDecal(string decalId, IReadOnlyCollection<DecalTexel> texels)
    {
        if (WorkshopVehicle() is not { } v) return Result.Fail("There's no car in your WorkShop.");
        var d = CI.Pack.Decals.Find(x => x.Id == decalId);
        if (d is null) return Result.Fail("Unknown decal.");
        if (DecalUses(decalId) < 1) return Result.Fail($"You are out of {d.Name} decals. Buy more in the Catalog.");
        if (Canvas(v) is not { } canvas) return Result.Fail("This car can't be painted that way.");
        State.DecalUses[decalId] = DecalUses(decalId) - 1;
        var paint = texels.Where(t => !Bare(v, t.SlotId)).Select(t => (t.X, t.Y, t.Color)).ToList();
        if (paint.Count > 0)
        {
            canvas.Put(paint);
            Emit(new PaintEvent(v.Id));
        }
        Changed();
        return Result.Done();
    }

    public Result<Unit> RemoveDecal(int index)
    {
        if (WorkshopVehicle() is not { } v || index < 0 || index >= v.Decals.Count) return Result.Fail("No such decal.");
        v.Decals.RemoveAt(index);
        Changed();
        return Result.Done();
    }

    // ---- vehicles ----------------------------------------------------------------------

    public VehicleState NewVehicle(CarModelDef car, Owner owner, double[] wear, double missingChance = 0)
    {
        var v = new VehicleState { Id = NewId("v"), ModelId = car.Id, Owner = owner, Paint = car.DefaultPaint };
        foreach (var s in car.Slots)
        {
            if (s.DefaultPart is null)
            {
                v.Slots[s.Id] = VehicleRules.EmptySlot();
                continue;
            }
            int cond = Rng.Weighted(Condition.All, c => wear[c]);
            v.Slots[s.Id] = VehicleRules.FilledSlot(CI, new PartInstance { Uid = NewId("p"), PartId = s.DefaultPart, Condition = cond, Extra = RandomExtra() }, tight: true);
        }
        if (missingChance > 0)
        {
            var leaves = car.Slots.Where(s => s.DefaultPart is not null && s.Openable is null && CI.Children(car.Id, s.Id).Count == 0).ToList();
            foreach (var s in leaves)
                if (Rng.Next() < missingChance) v.Slots[s.Id] = VehicleRules.EmptySlot();
        }
        State.Vehicles[v.Id] = v;
        return v;
    }

    /// <summary>Takes this share of a car's parts off, from the outside in: each time one with nothing left on it and
    /// nothing that has to come off first (so no part is left hanging on a missing one).</summary>
    void Strip(VehicleState v, double share)
    {
        var car = CI.Car(v.ModelId);
        int n = (int)Math.Round(share * car.Slots.Count(s => v.Slots[s.Id].Part is not null));
        for (int i = 0; i < n; i++)
        {
            var free = car.Slots.Where(s => v.Slots[s.Id].Part is { } p && VehicleRules.MountedChildren(CI, v, s.Id).Count == 0
                && (CI.Part(p.PartId).RemoveAfter ?? []).All(id => v.Slots.GetValueOrDefault(id)?.Part is null)).ToList();
            if (free.Count == 0) break;
            v.Slots[Rng.Pick(free).Id] = VehicleRules.EmptySlot();
        }
    }

    void SetWorkshop(string? id)
    {
        State.Workshop = id;
        if (id is not null && State.Vehicles.TryGetValue(id, out var v)) State.JunkFor = v.ModelId;
        Emit(new WorkshopEvent(id));
        CheckCarComplete();
    }

    /// <summary>Measured: the Car Lot has twelve bays.</summary>
    public const int LotBays = 12;

    /// <summary>Your cars: those on the lot and the one in the WorkShop (the sign-in sheet's CARS).</summary>
    public int CarsOwned => State.Lot.Count + (WorkshopVehicle() is { Owner: Owner.Player } ? 1 : 0);

    /// <summary>
    /// Parks one of your cars. The lot keeps your cars in its bays from the first, in the order of their Numbers:
    /// measured, the first car parked stayed in the first bay and every car bought after it went further along.
    /// A car of yours that has no Number yet gets the next one.
    /// </summary>
    void ParkInLot(string id)
    {
        if (State.Vehicles.GetValueOrDefault(id) is { Number: null, Owner: Owner.Player } car) car.Number = State.Stats.CarsBought++;
        State.Lot.Remove(id);
        int NumberOf(string x) => State.Vehicles.GetValueOrDefault(x)?.Number ?? int.MaxValue;
        int at = State.Lot.FindIndex(x => NumberOf(x) > NumberOf(id));
        State.Lot.Insert(at < 0 ? State.Lot.Count : at, id);
    }

    /// <summary>Bring one of your cars from the Car Lot into the Workshop.</summary>
    public Result<Unit> BringToWorkshop(string vehicleId)
    {
        if (State.Job is not null) return Result.Fail("Finish the job first.");
        int i = State.Lot.IndexOf(vehicleId);
        if (i < 0) return Result.Fail("That car is not on your lot.");
        State.Lot.RemoveAt(i);
        if (State.Workshop is { } current) ParkInLot(current);
        SetWorkshop(vehicleId);
        Changed();
        return Result.Done();
    }

    public Result<Unit> PutCarInLot()
    {
        if (State.Job is not null) return Result.Fail("That's a customer's car. Finish the job first.");
        if (State.Workshop is not { } id) return Result.Fail("There's no car in your WorkShop.");
        ParkInLot(id);
        SetWorkshop(null);
        Changed();
        return Result.Done();
    }

    /// <summary>
    /// Called by the UI clock. Measured: the sign-in's TOTAL TIME counts all the time, boxes up, the Catalog and the
    /// JunkYard included; the car's Repair Time only while the WorkShop is up without a box (<paramref name="atWork"/>;
    /// bolt mode counts: 30 s of each, the WorkShop idle and bolt mode counted in full, the Catalog, the Scrap Part box and
    /// the JunkYard not at all), and not once the car is complete (the same after minutes more in the WorkShop).
    /// </summary>
    public void Tick(double dt, bool atWork = true, bool auctionRuns = true)
    {
        State.PlayTime += dt;
        if (atWork && WorkshopVehicle() is { Owner: Owner.Player, Completed: false } v) v.Stats.RepairTime += dt;
        if (auctionRuns && State.Auction is { Closed: false }) TickAuction(dt);
    }

    // ---- jobs --------------------------------------------------------------------------

    /// <summary>"Get A Job": the next customer and their request. Answer with AcceptJob, or DeclineJob
    /// to think about it: the original offers a job pack's job again until you take it; a random job
    /// goes away and the next click brings a new one.</summary>
    public Result<Job> RequestJob()
    {
        if (State.Job is not null) return Result.Fail<Job>("You are already working on a job.");
        if (State.Offer is { } existing) return Result.Success(existing);
        var offer = NextScriptedJob() ?? MakeFreePlayJob();
        if (offer is null) return Result.Fail<Job>("Nobody needs a mechanic right now. Try again.");
        State.Offer = offer;
        Changed();
        return Result.Success(offer);
    }

    /// <summary>The Jobs Mode chain: scripted jobs in order, until the last one is done.</summary>
    public IEnumerable<JobTemplateDef> JobChain =>
        CI.Pack.Jobs.Where(j => j.Sequence is not null).OrderBy(j => j.Sequence);

    /// <summary>In Jobs Mode (the tutorial chain) the Car Lot, the Auction and your own cars wait.</summary>
    public bool InJobsMode => !State.FreePlay && JobChain.Any(j => !State.CompletedJobs.Contains(j.Id));

    Job? NextScriptedJob()
    {
        if (State.FreePlay) return null;
        var tpl = JobChain.FirstOrDefault(j => !State.CompletedJobs.Contains(j.Id));
        if (tpl is null)
        {
            if (JobChain.Any()) EnterFreePlay();
            return null;
        }
        return MakeScriptedJob(tpl);
    }

    void EnterFreePlay()
    {
        if (State.FreePlay) return;
        State.FreePlay = true;
        Log("That was the last job of Jobs Mode. Welcome to Free Play: buy, fix and sell cars as you like.", LogKind.Good);
    }

    /// <summary>The job packs' own jobs still open in Free Play: for cars your skill allows, each done once.
    /// Measured: they are offered cheapest first, and random jobs come only when none is left.</summary>
    public IEnumerable<JobTemplateDef> OpenPackJobs =>
        CI.Pack.Jobs.Where(j => j.Sequence is null && j.Weight > 0 && j.CarId is { } car && CI.HasCar(car)
            && CI.Car(car).MinSkill <= State.Skill && !State.CompletedJobs.Contains(j.Id)).OrderBy(j => j.Fee ?? 0);

    /// <summary>Random jobs: built from templates (ours), or in the original's words from a phrase book.</summary>
    List<JobTemplateDef> RandomJobPool()
    {
        var rules = CI.Pack.Rules;
        var allowed = State.Skill < rules.Skills.Count ? rules.Skills[State.Skill].Difficulties : [Difficulty.Easy];
        return CI.Pack.Jobs.Where(j => j.Sequence is null && j.Weight > 0 && j.CarId is null
            && (j.Phrased || allowed.Contains(j.Difficulty))).ToList();
    }

    Job? MakeFreePlayJob()
    {
        if (OpenPackJobs.FirstOrDefault() is { } next) return MakeScriptedJob(next);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var pool = RandomJobPool();
            if (pool.Count == 0) return null;
            var tpl = Rng.Weighted(pool, j => j.Weight);
            var job = tpl.Phrased ? (CI.Pack.Phrases is { } book ? MakePhrasedJob(tpl, book) : null) : MakeRandomJob(tpl);
            if (job is not null) return job;
        }
        return null;
    }

    /// <summary>A job from a job pack: a fixed car, fee, texts and start and finish states.</summary>
    public Job MakeScriptedJob(JobTemplateDef tpl)
    {
        var car = CI.Car(tpl.CarId ?? throw new InvalidDataException($"job {tpl.Id} has no car"));
        // Measured: a job's car is its start state and nothing more. The packs start with the whole stock car ("all"),
        // but for a Checker Cab whose owner has taken the old engine out (running gear and body only).
        // A pack job with no start state at all gets the whole stock car, in good shape.
        VehicleState v;
        if (tpl.Start is not { Count: > 0 }) v = NewVehicle(car, Owner.Customer, [0, 0, 0, 1]);
        else
        {
            v = new VehicleState { Id = NewId("v"), ModelId = car.Id, Owner = Owner.Customer, Paint = car.DefaultPaint };
            foreach (var slot in car.Slots) v.Slots[slot.Id] = VehicleRules.EmptySlot();
            State.Vehicles[v.Id] = v;
        }
        if (tpl.Paint is { } paint) v.Paint = paint;
        var removed = new Dictionary<string, PartInstance>();
        foreach (var st in tpl.Start ?? []) ApplyStat(v, car, st, removed);
        var reqs = new List<JobReq>();
        // A region's condition counts only where the job lets you work (a job on the ENGINE tab alone that asks for "the
        // car green" means its engine).
        bool Allowed(SlotDef s) => tpl.Tabs is null || tpl.Tabs.Contains(s.Region.ToView());
        foreach (var st in tpl.Complete ?? [])
        {
            if (st.Region is { } region)
            {
                var slots = car.Slots.Where(s => s.Required && InRegion(s, region) && Allowed(s)).Select(s => s.Id).ToList();
                if (slots.Count > 0) reqs.Add(new JobReq { Type = JobReqType.Fix, SlotIds = slots, MinCondition = Math.Max(0, st.Condition) });
            }
            else if (st.Part is { } pid && CI.TryPart(pid, out var part) && car.Slots.Find(s => s.SlotType == part.SlotType) is { } slot)
            {
                reqs.Add(st.Condition == JobStatDef.Absent
                    ? new JobReq { Type = JobReqType.Remove, SlotIds = [slot.Id], Parts = [pid] }
                    : new JobReq { Type = JobReqType.Install, SlotIds = [slot.Id], Parts = [pid], MinCondition = st.Condition });
            }
        }
        var names = CI.Pack.Names;
        string customer = names.First.Count > 0 ? $"{Rng.Pick(names.First)} {Rng.Pick(names.Last)}" : "Customer";
        var fee = tpl.Fee ?? JobFee(CI, car.Id, reqs.Where(r => r.Type == JobReqType.Install).ToList(), tpl.Difficulty, tpl);
        return new Job
        {
            Id = NewId("job"),
            TemplateId = tpl.Id,
            Customer = customer,
            Portrait = tpl.Portrait ?? (CI.Pack.Portraits.Count > 0 ? Rng.Pick(CI.Pack.Portraits) : ""),
            ThanksPortrait = tpl.ThanksPortrait,
            Text = tpl.Text,
            Nag = tpl.Hints?.FirstOrDefault() ?? tpl.Nag ?? Rng.Pick(DefaultNags).Replace("{car}", car.Name),
            Hints = tpl.Hints,
            Thanks = tpl.Thanks ?? Rng.Pick(DefaultThanks),
            Difficulty = DifficultyOf(CI.Pack.Rules, fee),
            Fee = fee,
            Budget = fee,
            VehicleId = v.Id,
            Reqs = reqs,
            Tabs = tpl.Tabs,
        };
    }

    static bool InRegion(SlotDef s, string region) => region switch
    {
        "all" => true,
        "engine" => s.Region == Region.Engine,
        "body" => s.Region == Region.Body,
        "running_gear" => s.Region == Region.RunningGear,
        _ => false,
    };

    /// <summary>One line of a job's start state, applied over the car; <paramref name="removed"/> keeps what the start
    /// took off.</summary>
    void ApplyStat(VehicleState v, CarModelDef car, JobStatDef st, Dictionary<string, PartInstance> removed)
    {
        if (st.Region is { } region)
        {
            foreach (var s in car.Slots.Where(s => s.DefaultPart is not null && InRegion(s, region)))
            {
                var cur = v.Slots[s.Id].Part;
                if (cur is not null && cur.PartId != s.DefaultPart) continue; // custom parts are listed one by one
                if (st.Condition == JobStatDef.Absent) v.Slots[s.Id] = VehicleRules.EmptySlot();
                else if (cur is null) v.Slots[s.Id] = VehicleRules.FilledSlot(CI, new PartInstance { Uid = NewId("p"), PartId = s.DefaultPart!, Condition = st.Condition, Extra = RandomExtra() }, tight: true);
                else cur.Condition = st.Condition;
            }
            return;
        }
        if (st.Part is not { } pid || !CI.TryPart(pid, out var part) || car.Slots.Find(s => s.SlotType == part.SlotType) is not { } slot) return;
        var here = v.Slots[slot.Id].Part;
        if (st.Condition == JobStatDef.Absent)
        {
            // Measured: what is mounted on it goes with it (the Escort's Cab Roof missing: both windshields and the trunk
            // were missing too, and had to be fitted).
            if (here?.PartId == pid) TakeOffWithWhatIsOnIt(v, slot.Id, removed);
        }
        else if (here?.PartId == pid) here.Condition = st.Condition;
        else
        {
            // Measured: a part the start puts on brings back what it goes on. The Escort's overheating job takes its Cab
            // Roof off, then puts the Hatchback Cosworth and the Front Windshield back on it: the original's car came in
            // with its roof on (and ASSEMBLED), no part floating where the roof was.
            PutBackUnder(v, car, slot, part, removed);
            v.Slots[slot.Id] = VehicleRules.FilledSlot(CI, new PartInstance { Uid = NewId("p"), PartId = pid, Condition = st.Condition, Extra = RandomExtra() }, tight: true);
        }
    }

    void TakeOffWithWhatIsOnIt(VehicleState v, string slotId, Dictionary<string, PartInstance> removed, HashSet<string>? done = null)
    {
        done ??= [];
        if (!done.Add(slotId)) return;
        foreach (var kid in VehicleRules.MountedChildren(CI, v, slotId)) TakeOffWithWhatIsOnIt(v, kid, removed, done);
        if (v.Slots[slotId].Part is { } p) removed[slotId] = p;
        v.Slots[slotId] = VehicleRules.EmptySlot();
    }

    /// <summary>The parts a part goes on, put back if they are off: the ones the start took off as they were, else the
    /// car's own in good shape (and what they go on, in turn).</summary>
    void PutBackUnder(VehicleState v, CarModelDef car, SlotDef slot, PartDef part, Dictionary<string, PartInstance> removed, HashSet<string>? seen = null)
    {
        seen ??= [];
        bool Filled(string id) => v.Slots.TryGetValue(id, out var ps) && ps.Part is not null;
        var under = slot.Parents.Concat(part.MountsAny ? [] : part.Mounts ?? []).ToList();
        if (part.MountsAny && part.Mounts is { Count: > 0 } any && !any.Any(Filled)) under.Add(any[0]);
        foreach (var id in under.Distinct())
        {
            if (!seen.Add(id) || Filled(id) || car.Slots.Find(s => s.Id == id) is not { } below) continue;
            var back = removed.Remove(id, out var was) ? was
                : below.DefaultPart is { } d ? new PartInstance { Uid = NewId("p"), PartId = d, Condition = Condition.Good, Extra = RandomExtra() } : null;
            if (back is null) continue;
            PutBackUnder(v, car, below, CI.Part(back.PartId), removed, seen);
            v.Slots[id] = VehicleRules.FilledSlot(CI, back, tight: true);
        }
    }

    /// <summary>A random job from a template, on a random car the template fits.</summary>
    Job? MakeRandomJob(JobTemplateDef tpl)
    {
        var pack = CI.Pack;
        var rules = pack.Rules;
        var carPool = pack.Cars.Where(c => c.MinSkill <= State.Skill && (tpl.BodyStyles is null || tpl.BodyStyles.Contains(c.BodyStyle))).ToList();
        if (carPool.Count == 0) return null;
        var car = Rng.Pick(carPool);
        var v = NewVehicle(car, Owner.Customer, rules.JobWearWeights);
        var used = new HashSet<string>();
        var reqs = new List<JobReq>();
        foreach (var rq in tpl.Requirements)
        {
            var cands = car.Slots.Where(s => rq.Families.Contains(s.Family) && !used.Contains(s.Id)).ToList();
            if (rq.Type == JobReqType.Install)
                cands = cands.Where(s => rq.Parts?.Any(p => CI.HasPart(p) && CI.Part(p).SlotType == s.SlotType) == true).ToList();
            if (rq.Type == JobReqType.Missing)
                cands = cands.Where(s => CI.Children(car.Id, s.Id).Count == 0).ToList();
            if (cands.Count < rq.Count[0])
            {
                State.Vehicles.Remove(v.Id);
                return null;
            }
            int n = Math.Min(cands.Count, Rng.Int(rq.Count[0], rq.Count[1]));
            var chosen = Rng.Shuffle(cands).Take(n).ToList();
            if (chosen.Count == 0) continue;
            foreach (var s in chosen)
            {
                used.Add(s.Id);
                var st = v.Slots[s.Id];
                if (rq.Type == JobReqType.Fix && st.Part is not null)
                    st.Part.Condition = Rng.Weighted(Condition.All, c => rules.JobDamageWeights[c]);
                if (rq.Type == JobReqType.Missing) v.Slots[s.Id] = VehicleRules.EmptySlot();
            }
            var parts = rq.Type == JobReqType.Install
                ? rq.Parts?.Where(p => CI.HasPart(p) && chosen.Any(s => CI.Part(p).SlotType == s.SlotType)).ToList()
                : null;
            reqs.Add(new JobReq { Type = rq.Type, SlotIds = chosen.Select(s => s.Id).ToList(), Parts = parts });
        }
        if (reqs.Count == 0)
        {
            State.Vehicles.Remove(v.Id);
            return null;
        }
        string customer = $"{Rng.Pick(pack.Names.First)} {Rng.Pick(pack.Names.Last)}";
        var fee = JobFee(CI, car.Id, reqs, tpl.Difficulty, tpl);
        string firstName = customer.Split(' ')[0];
        string Fill(string t) => t.Replace("{name}", firstName).Replace("{car}", $"{car.Make} {car.Name}".Trim());
        return new Job
        {
            Id = NewId("job"),
            TemplateId = tpl.Id,
            Customer = customer,
            Portrait = pack.Portraits.Count > 0 ? Rng.Pick(pack.Portraits) : "",
            Text = Fill(tpl.Text),
            Nag = Fill(tpl.Nag ?? Rng.Pick(DefaultNags)),
            Thanks = Fill(tpl.Thanks ?? Rng.Pick(DefaultThanks)),
            Difficulty = DifficultyOf(rules, fee),
            Fee = fee,
            Budget = fee,
            VehicleId = v.Id,
            Reqs = reqs,
            Tabs = tpl.Tabs,
        };
    }

    /// <summary>
    /// How a random job's car comes in. Measured on 482 different offers of the original (their words split
    /// with the phrase file, their fees read): what is wrong is nothing (the customer only says hello, fee $0),
    /// the whole car, one region, two regions (told in the order engine, body, running gear) or one or two
    /// parts. How much of a region is damaged and in which colours is fitted to the fees of each kind.
    /// </summary>
    static class RandomJobOdds
    {
        public enum Kind { Nothing, Whole, OneRegion, TwoRegions, OnePart, TwoParts }
        public static readonly Kind[] Kinds = [Kind.Nothing, Kind.Whole, Kind.OneRegion, Kind.TwoRegions, Kind.OnePart, Kind.TwoParts];
        /// <summary>Measured: 1.7 %, 23.7 %, 29.0 %, 38.4 %, 2.5 %, 4.8 % (482 offers, most of them a Novice's).</summary>
        public static readonly double[] KindOdds = [0.017, 0.237, 0.290, 0.384, 0.025, 0.047];
        /// <summary>At the top skill the whole car comes more often: 0.8 %, 38.7 %, 22.1 %, 32.2 %, 1.8 %, 4.4 %
        /// (494 offers of a Mekada).</summary>
        public static readonly double[] TopKindOdds = [0.008, 0.387, 0.221, 0.322, 0.018, 0.044];
        /// <summary>The customer's opening and closing words, each on its own (measured: 49 % and 54 %; an offer
        /// with nothing wrong can come with no words at all).</summary>
        public const double Opening = 0.49, Closing = 0.54;
        /// <summary>A job tells symptoms rather than causes, in every region the phrase file has symptoms for
        /// (measured: never a symptom in one region and a cause in the other where both were possible).</summary>
        public const double Symptoms = 0.5;
        /// <summary>The region of one damaged region: engine, body, running gear (measured 31, 36, 33 %).</summary>
        public static readonly double[] RegionOdds = [0.31, 0.36, 0.33];
        /// <summary>Two regions: engine and body, engine and running gear, body and running gear (measured 32, 25, 43 %).</summary>
        public static readonly double[] PairOdds = [0.32, 0.25, 0.43];
        /// <summary>A region's condition words (the middle ones never came up, in 574 offers of a Mekada) don't follow
        /// its colours (the mildest with a black part among its damaged ones, the strongest with none): the strongest in
        /// two-region jobs (98 % measured), for one region about a third of the time (38 %), for the whole car a
        /// little over half (56 %). A part's words follow its colour (the strongest for a black part: red and yellow
        /// ones always got the mildest, a black one the strongest but once).</summary>
        public const double StrongTwo = 0.98, StrongOne = 0.38, StrongWhole = 0.56;
        /// <summary>How much of a region is damaged, by quarters of its parts. Measured on 80 offers whose damaged
        /// parts the original listed in its log: one region 32, 36, 14, 18 % (22 offers), each of two regions 30, 30,
        /// 26, 15 % (54 regions), each region of the whole car 22, 19, 22, 37 % (78 regions).</summary>
        public static readonly double[] OneShare = [0.32, 0.36, 0.14, 0.18], TwoShare = [0.30, 0.30, 0.26, 0.15], WholeShare = [0.22, 0.19, 0.22, 0.37];
        /// <summary>Fewest parts damaged in a region: three when the region is told on its own (its "how many" words
        /// said "one or two" of three), one in two-region jobs and each region of the whole car (measured).</summary>
        public const int FewestOne = 3, FewestOther = 1;
        /// <summary>In the top quarter every one of the region's parts, half the time (14 of the whole car's 29
        /// regions there were damaged throughout).</summary>
        public const double AllOfTop = 0.5;
        /// <summary>Colours of a region's damaged parts, black, red, yellow, fitted to 153 offers' fees one by one (every
        /// colouring of the damaged parts the log names that can make the fee, the odds that make them likeliest):
        /// a region told alone 22, 43, 35 % (408 parts), each of two regions 27, 30, 43 % (940 parts); the whole car's
        /// and a single part's as fitted on the fees' averages before (a region's came to 1.33 times its repairs all in
        /// red, the whole car's 1.26; a part named got the strongest words 61 % of the time): the fees one by one
        /// don't settle those (too few whole cars with a countable number of parts).</summary>
        public static readonly double[] OneLevels = [0.22, 0.43, 0.35], TwoLevels = [0.27, 0.30, 0.43],
            WholeLevels = [0.14, 0.64, 0.22], PartLevels = [0.5, 0.15, 0.35];
        /// <summary>Of two parts, the second one missing (3 of the 23 two-part offers).</summary>
        public const double SecondMissing = 0.13;
    }

    sealed record Trouble(PhraseKind Kind, PhraseTopic Topic, JobReq Req);

    /// <summary>
    /// A random job the way the original makes them: a car your skill allows comes in with nothing, the whole
    /// car, one or two regions, or one or two parts wrong (<see cref="RandomJobOdds"/>), and one of the phrase
    /// book's customers says so in its words. Finished when every one of those parts is back and green.
    /// Measured: the fee is 1.25 × what it takes to put the car right, priced with random extras of its own (a
    /// repair in the fee comes out a little different from the one you pay), the difficulty word follows the
    /// fee, the words are joined as <see cref="PhraseWriter.Join"/> does, two troubles run together with a word
    /// from the book's %And, a part is named in lower case, and a job with nothing wrong is done as soon as you
    /// take it.
    /// </summary>
    Job? MakePhrasedJob(JobTemplateDef tpl, JobPhraseBook book)
    {
        var pack = CI.Pack;
        var carPool = pack.Cars.Where(c => c.MinSkill <= State.Skill).ToList();
        if (carPool.Count == 0 || book.Customers.Count == 0) return null;
        var car = Rng.Pick(carPool);
        var v = NewVehicle(car, Owner.Customer, [0, 0, 0, 1]);
        bool Has(PhraseKind k, View? r = null) => book.Sentences.Any(s => s.Kind == k && (r is null || s.Region == r));
        var used = new HashSet<string>();
        List<SlotDef> Present(View region) => car.Slots.Where(s =>
            s.DefaultPart is not null && v.Slots[s.Id].Part is not null && (region == View.Complete || s.Region.ToView() == region)).ToList();
        int Pick(double[] odds) => Rng.Weighted(Enumerable.Range(0, odds.Length).ToList(), i => odds[i]);
        int Level(double[] odds) => Pick(odds) switch { 0 => Condition.Black, 1 => Condition.Red, _ => Condition.Yellow };
        var symptoms = Rng.Next() < RandomJobOdds.Symptoms;
        PhraseKind Tell(View region)
        {
            var (want, other) = symptoms ? (PhraseKind.Symptom, PhraseKind.Cause) : (PhraseKind.Cause, PhraseKind.Symptom);
            return Has(want, region) ? want : Has(other, region) ? other : PhraseKind.RegionShare;
        }

        // Damage in a region: a share of its parts, drawn by quarters, in the region's colours.
        (List<SlotDef> Hit, int Of) Damage(View region, double[] shares, int fewest, double[] levels)
        {
            var all = Present(region).Where(s => !used.Contains(s.Id)).ToList();
            if (all.Count == 0) return ([], 0);
            int band = Pick(shares);
            double share = band == 3 && Rng.Next() < RandomJobOdds.AllOfTop ? 1 : Rng.Range(band / 4.0, (band + 1) / 4.0);
            int k = Math.Clamp((int)Math.Round(all.Count * share), Math.Min(fewest, all.Count), all.Count);
            var hit = Rng.Shuffle(all).Take(k).ToList();
            foreach (var s in hit)
            {
                v.Slots[s.Id].Part!.Condition = Level(levels);
                used.Add(s.Id);
            }
            return (hit, all.Count);
        }
        Trouble? Region(View region, PhraseKind kind, double[] shares, double strong, int fewest, double[] levels)
        {
            var (hit, of) = Damage(region, shares, fewest, levels);
            if (hit.Count == 0) return null;
            var topic = new PhraseTopic(region, Rng.Next() < strong ? 1 : 0, hit.Count / (double)of, hit.Count);
            return new Trouble(kind, topic, new JobReq { Type = JobReqType.Fix, SlotIds = hit.Select(s => s.Id).ToList() });
        }
        Trouble? Part(bool missing)
        {
            missing &= Has(PhraseKind.PartMissing);
            if (!missing && !Has(PhraseKind.PartDamaged)) return null;
            var cands = Present(View.Complete).Where(s => !used.Contains(s.Id)
                && (!missing || (s.Openable is null && CI.Children(car.Id, s.Id).Count == 0))).ToList();
            if (cands.Count == 0) return null;
            var s = Rng.Pick(cands);
            used.Add(s.Id);
            int level = missing ? Condition.Black : Level(RandomJobOdds.PartLevels);
            if (missing) v.Slots[s.Id] = VehicleRules.EmptySlot();
            else v.Slots[s.Id].Part!.Condition = level;
            // Measured: the part is named in lower case ("my b axle", "the handlebar mount").
            var name = CI.Part(s.DefaultPart!).Name.ToLowerInvariant();
            var topic = new PhraseTopic(s.Region.ToView(), level == Condition.Black ? 1 : 0, 1.0 / Present(s.Region.ToView()).Count, 1, name);
            return new Trouble(missing ? PhraseKind.PartMissing : PhraseKind.PartDamaged, topic,
                new JobReq { Type = missing ? JobReqType.Missing : JobReqType.Fix, SlotIds = [s.Id] });
        }

        var kind = RandomJobOdds.Kinds[Pick(State.Skill >= pack.Rules.Skills.Count - 1 ? RandomJobOdds.TopKindOdds : RandomJobOdds.KindOdds)];
        var troubles = new List<Trouble?>();
        View[] regions = [View.Engine, View.Body, View.RunningGear];
        switch (kind)
        {
            case RandomJobOdds.Kind.Whole:
            {
                // The whole car: every region damaged, told in one sentence about the whole car.
                var hits = regions.Select(r => Damage(r, RandomJobOdds.WholeShare, RandomJobOdds.FewestOther, RandomJobOdds.WholeLevels)).ToList();
                var slots = hits.SelectMany(h => h.Hit).Select(s => s.Id).ToList();
                if (slots.Count == 0)
                {
                    troubles.Add(null);
                    break;
                }
                var topic = new PhraseTopic(View.Complete, Rng.Next() < RandomJobOdds.StrongWhole ? 1 : 0,
                    slots.Count / (double)Math.Max(1, hits.Sum(h => h.Of)), slots.Count);
                troubles.Add(new Trouble(Tell(View.Complete), topic, new JobReq { Type = JobReqType.Fix, SlotIds = slots }));
                break;
            }
            case RandomJobOdds.Kind.OneRegion:
            {
                var r = regions[Pick(RandomJobOdds.RegionOdds)];
                troubles.Add(Region(r, Has(PhraseKind.RegionShare) ? PhraseKind.RegionShare : Tell(r), RandomJobOdds.OneShare, RandomJobOdds.StrongOne, RandomJobOdds.FewestOne, RandomJobOdds.OneLevels));
                break;
            }
            case RandomJobOdds.Kind.TwoRegions:
            {
                View[][] pairs = [[View.Engine, View.Body], [View.Engine, View.RunningGear], [View.Body, View.RunningGear]];
                foreach (var r in pairs[Pick(RandomJobOdds.PairOdds)])
                    troubles.Add(Region(r, Tell(r), RandomJobOdds.TwoShare, RandomJobOdds.StrongTwo, RandomJobOdds.FewestOther, RandomJobOdds.TwoLevels));
                break;
            }
            case RandomJobOdds.Kind.OnePart:
                troubles.Add(Part(missing: false));
                break;
            case RandomJobOdds.Kind.TwoParts:
            {
                var a = Part(missing: false);
                var b = Part(missing: Rng.Next() < RandomJobOdds.SecondMissing);
                // Told in the order of the regions (engine, body, running gear).
                troubles.AddRange(a is not null && b is not null && b.Topic.Region < a.Topic.Region ? [b, a] : [a, b]);
                break;
            }
        }
        if (troubles.Any(t => t is null))
        {
            State.Vehicles.Remove(v.Id);
            return null;
        }
        var told = troubles.OfType<Trouble>().ToList();

        var w = new PhraseWriter(book, n => Rng.Int(0, n - 1));
        string Sentence(Trouble t)
        {
            var lines = book.Sentences.Where(s => s.Kind == t.Kind && (s.Region is null || s.Region == t.Topic.Region)).ToList();
            return w.Expand(Rng.Pick(lines).Text, t.Topic);
        }
        var who = Rng.Pick(book.Customers);
        var first = told.Count > 0 ? told[0].Topic : new PhraseTopic(View.Complete, 0, 0, 0);
        var said = told.Select(Sentence).ToList();
        // Two troubles make one piece, run together with %And: "... is loud. plus, i ..." (which the original
        // writes "... is loud.  Plus, i ...").
        string and = book.Variables.ContainsKey("And") ? w.Expand("%And", first) : "also,";
        string damage = said.Count switch { 0 => "", 1 => said[0], _ => $"{said[0]} {and} {said[1]}" };
        bool opening = Rng.Next() < RandomJobOdds.Opening, closing = Rng.Next() < RandomJobOdds.Closing;
        var reqs = told.Select(t => t.Req).ToList();
        var fee = Cents(RandomJobFeeFactor * FeeCost(v, reqs.SelectMany(r => r.SlotIds)));
        var names = pack.Names;
        string customer = names.First.Count > 0 ? $"{Rng.Pick(names.First)} {Rng.Pick(names.Last)}" : "Customer";
        var views = told.Select(t => t.Topic.Region).Distinct().ToList();
        var text = PhraseWriter.Join([opening ? w.Expand(who.Before, first) : "", damage, closing ? w.Expand(who.After, first) : ""]);
        return new Job
        {
            Id = NewId("job"),
            TemplateId = tpl.Id,
            Customer = customer,
            Portrait = who.Face,
            ThanksPortrait = who.HappyFace.Length > 0 ? who.HappyFace : null,
            Text = text,
            // Measured: Job Update shows only what is wrong, its sentences joined with single spaces.
            Nag = said.Count > 0 ? PhraseWriter.Hint(damage) : text,
            Thanks = PhraseWriter.Join([w.Expand(who.Thanks, first)]),
            Difficulty = DifficultyOf(pack.Rules, fee),
            Fee = fee,
            Budget = fee,
            VehicleId = v.Id,
            Reqs = reqs,
            Tabs = views.Contains(View.Complete) ? null : [View.Complete, .. views.Order()],
        };
    }

    /// <summary>What a random job's fee is made from: <see cref="Economy.FixCost"/> of the damaged places, each
    /// damaged part priced with a random extra of its own. Measured on three offers: the fee was 1.25 × a repair
    /// of the part's colour whose extra was not the one the part turned up with (a yellow $10–100 part that
    /// cost $12.32 to repair, in a $19.26 fee); black and missing parts are the catalog price either way.</summary>
    decimal FeeCost(VehicleState v, IEnumerable<string> slotIds)
    {
        var ids = slotIds.ToList();
        var priced = new VehicleState { ModelId = v.ModelId };
        foreach (var id in ids)
            priced.Slots[id] = v.Slots[id].Part is { } p
                ? VehicleRules.FilledSlot(CI, new PartInstance { PartId = p.PartId, Condition = p.Condition, Extra = RandomExtra() }, tight: true)
                : VehicleRules.EmptySlot();
        return FixCost(CI, priced, ids);
    }

    public Result<Job> AcceptJob()
    {
        if (State.Offer is not { } offer) return Result.Fail<Job>("There is no job offer.");
        State.Offer = null;
        if (State.Workshop is { } current) ParkInLot(current);
        offer.StartVehicle = Json.Write(Vehicle(offer.VehicleId));
        offer.StartBin = State.Bin.Select(b => b.Part.Uid).ToList();
        State.Job = offer;
        SetWorkshop(offer.VehicleId);
        var car = Vehicle(offer.VehicleId);
        if (!State.Shelves.ContainsKey(car.ModelId)) NewShelf(car.ModelId, car, won: false);
        Log($"{offer.Customer} brought in a {CarName(Vehicle(offer.VehicleId))}. Budget: {Money(offer.Fee)}.", LogKind.Info, toast: false);
        Changed();
        return Result.Success(offer);
    }

    /// <summary>A job that asks for nothing (a random customer who only says hello) is done as soon as it is
    /// taken (measured). The UI calls this once the car is on show.</summary>
    public void FinishJobIfDone() => AfterWork();

    /// <summary>CANCEL on the Job Request. Measured: a job pack's customer is still waiting (the same offer
    /// comes back); a random customer goes away and the next Get A Job brings a new one.</summary>
    public Result<Unit> DeclineJob()
    {
        if (State.Offer is not { } offer) return Result.Fail("There is no job offer.");
        if (CI.Pack.Jobs.Find(j => j.Id == offer.TemplateId) is not { CarId: not null })
        {
            State.Vehicles.Remove(offer.VehicleId);
            State.Offer = null;
            Changed();
        }
        return Result.Done();
    }

    /// <summary>Give up (Job Help's CANCEL in Free Play): the customer takes the car back and the
    /// budget is gone.</summary>
    public Result<Unit> QuitJob()
    {
        if (State.Job is not { } job) return Result.Fail("You are not on a job.");
        DropJobCar(job);
        ForgetShelves(job);
        State.Stats.JobsQuit++;
        Log($"{job.Customer} took the car to another garage.", LogKind.Bad);
        Changed();
        return Result.Done();
    }

    /// <summary>Exit during a job (after the Job Active box): the job goes as Job Help's RESTART (Jobs Mode) or CANCEL
    /// would take it, and the JunkYard shelves first stocked for it are not kept.</summary>
    public Result<Unit> LeaveJob()
    {
        if (State.Job is not { } job) return Result.Fail("You are not on a job.");
        var r = InJobsMode ? RestartJob().Ok : QuitJob().Ok;
        ForgetShelves(job);
        return r ? Result.Done() : Result.Fail("You are not on a job.");
    }

    /// <summary>Measured: the original saves nothing when a job is given up (Job Help's CANCEL) or left (Exit), so a
    /// shelf first stocked for its car is not there when the mechanic comes back (six new models' yards visited and given
    /// up: none saved; a '23 Ford T's left by Exit: not saved).</summary>
    void ForgetShelves(Job job)
    {
        foreach (var model in job.NewShelves ?? []) State.Shelves.Remove(model);
        job.NewShelves = null;
    }

    /// <summary>The job car leaves, and with it what was bought for the job.</summary>
    void DropJobCar(Job job)
    {
        State.Job = null;
        State.Vehicles.Remove(job.VehicleId);
        if (job.StartBin is { } keep) State.Bin.RemoveAll(b => !keep.Contains(b.Part.Uid));
        State.PurchaseBin.Clear();
        SetWorkshop(null);
    }

    /// <summary>
    /// Job Help's RESTART (Jobs Mode): the customer brings the car in again. Measured: no questions,
    /// the Job Request comes back, the budget is full, what you bought for the job is gone, and the
    /// damage is dealt again (repair prices come out a little different).
    /// </summary>
    public Result<Job> RestartJob()
    {
        if (State.Job is not { StartVehicle: { } start } job) return Result.Fail<Job>("You are not on a job.");
        DropJobCar(job);
        var v = Json.Parse<VehicleState>(start);
        FitVehicle(v);
        v.Id = NewId("v");
        foreach (var st in v.Slots.Values)
            if (st.Part is { } p)
            {
                p.Uid = NewId("p");
                p.Extra = RandomExtra();
            }
        State.Vehicles[v.Id] = v;
        var offer = Json.Parse<Job>(Json.Write(job));
        offer.Id = NewId("job");
        offer.VehicleId = v.Id;
        offer.Budget = offer.Fee;
        offer.HintIndex = 0;
        offer.StartVehicle = null;
        offer.StartBin = null;
        offer.NewShelves = null; // taking the job again saves them (the original saves on a job's start)
        State.Offer = offer;
        Log("The customer brings the car in again.", LogKind.Info, toast: false);
        Changed();
        return Result.Success(offer);
    }

    /// <summary>Job Help: the next hint. They come round in turn (1, 2, 1, 2...).</summary>
    public string NextHint()
    {
        if (State.Job is not { } job) return "";
        if (job.Hints is not { Count: > 0 } hints) return job.Nag;
        var text = hints[job.HintIndex % hints.Count];
        job.HintIndex = (job.HintIndex + 1) % hints.Count;
        return text;
    }

    /// <summary>What is still missing for the current job (empty = done).</summary>
    public (bool Done, List<string> Open) JobProgress()
    {
        if (State.Job is not { } job) return (false, []);
        var v = Vehicle(job.VehicleId);
        var open = new List<string>();
        foreach (var rq in job.Reqs)
            foreach (var id in rq.SlotIds)
            {
                var p = v.Slots[id].Part;
                string name = CI.Slot(v.ModelId, id).Name;
                if (rq.Type == JobReqType.Remove)
                {
                    if (p is not null && (rq.Parts is null || rq.Parts.Contains(p.PartId))) open.Add($"{name}: the customer wants it off");
                    continue;
                }
                if (p is null) open.Add($"{name}: missing");
                else if (rq.Parts is not null && !rq.Parts.Contains(p.PartId)) open.Add($"{name}: not the part the customer asked for");
                else if (p.Condition < rq.MinCondition) open.Add($"{name}: not fixed");
            }
        // Assembled where the job lets you work (a region it keeps you out of stays as the customer brought it).
        foreach (var reg in Regions.All)
            if ((job.Tabs is null || job.Tabs.Contains(reg.ToView())) && !VehicleRules.IsComplete(CI, v, reg)) open.Add($"{reg.Label()} not assembled");
        return (open.Count == 0, open);
    }

    /// <summary>
    /// The job finishes by itself once the car meets the request. Measured payout: the fee plus what is
    /// left of the budget. (The measured game also dropped the cents of your cash at that point; we take
    /// that for the same compatibility bug as the whole-dollar charges and don't copy it.)
    /// </summary>
    void CheckJob()
    {
        if (State.Job is not { } job || !JobProgress().Done) return;
        var paid = job.Fee + job.Budget;
        var model = Vehicle(job.VehicleId).ModelId;
        State.Job = null;
        State.Stats.JobsDone++;
        State.Stats.CarsRepaired++;
        if (CI.Pack.Jobs.Find(j => j.Id == job.TemplateId) is { } tpl && (tpl.Sequence is not null || tpl.CarId is not null)
            && !State.CompletedJobs.Contains(tpl.Id))
        {
            State.CompletedJobs.Add(tpl.Id);
            if (tpl.Sequence is not null && !JobChain.Any(j => !State.CompletedJobs.Contains(j.Id))) EnterFreePlay();
        }
        ChurnShelf(model);
        // First, while the car is still in the WorkShop: the UI shows it as the job leaves it (the last part on it).
        Emit(new JobDoneEvent(job, paid, model));
        State.Vehicles.Remove(job.VehicleId);
        SetWorkshop(null);
        Log($"Job complete! {job.Customer} paid {Money(job.Fee)} and you keep the {Money(job.Budget)} left of the budget.", LogKind.Good);
        payingJob = true;
        Receive(paid, Account.Cash);
        payingJob = false;
    }

    /// <summary>A job's pay is coming in (a skill it earns comes up over a blank WorkShop).</summary>
    bool payingJob;

    // ---- auction -----------------------------------------------------------------------

    /// <summary>How long one car takes to drive off the Auction's stage and the next to drive on, before the next one's
    /// figures come up (measured on 60 fps films: the old car gone in ~0.3 s, the new one in place ~0.75 s after the
    /// time ran out; the same after Skip Car).</summary>
    public const double AuctionSwap = 0.75;

    AuctionState NewAuction(AuctionMode mode, VehicleState v, bool driveOn = false)
    {
        var r = CI.Pack.Rules;
        var value = Economy.CarValue(CI, v);
        var opening = r.AuctionOpeningBid;
        // Measured: the step follows the car as it is (see Economy.AuctionStep), so a car unchanged keeps it.
        var step = Economy.AuctionStep(CI, v);
        v.AuctionStep = step;
        v.AuctionStepValue = value;
        var a = new AuctionState
        {
            Mode = mode,
            VehicleId = v.Id,
            CurrentBid = opening,
            Asking = opening + step,
            Step = step,
            // Measured: the rivals' limit is so many steps above the opening, drawn anew each time the car goes up, and
            // not held to what is on the car (a Miata with two parts worth $92 went for $640).
            RivalMax = opening + step * (decimal)RivalSteps(),
            Duration = r.AuctionDuration,
            // Measured: in most cars the first bid comes at once, a tenth of a second or two after the first drop.
            NextRivalIn = Rng.Next() < AuctionQuickStart ? Rng.Range(0.05, 0.2) : NextRivalDelay(0),
            Arriving = driveOn ? AuctionSwap : 0,
        };
        if (!driveOn) Drop(a); // the first drop comes as the car goes up (else as it has driven on)
        return a;
    }

    /// <summary>How often the other bidders open at once (8 of 11 cars watched).</summary>
    const double AuctionQuickStart = 0.75;

    /// <summary>How many steps above the opening the other bidders will go this time: drawn from the spread measured
    /// at your skill (see <see cref="RulesDef.AuctionRivalSpread"/>), between its quartiles in a straight line.</summary>
    double RivalSteps()
    {
        var r = CI.Pack.Rules;
        if (r.AuctionRivalSpread is not { Length: > 0 } spread) return Rng.Range(r.AuctionRivalSteps[0], r.AuctionRivalSteps[^1]);
        var q = spread[Math.Clamp(State.Skill, 0, spread.Length - 1)];
        if (q.Length == 1) return q[0];
        double x = Rng.Next() * (q.Length - 1);
        int i = Math.Min((int)x, q.Length - 2);
        return q[i] + (q[i + 1] - q[i]) * (x - i);
    }

    double NextRivalDelay(double elapsed)
    {
        var r = CI.Pack.Rules;
        // Measured: the rivals bid on a beat, now and then letting one go by, all the way (the gaps look longer near
        // the end only because the asking price has gone past what they pay until a drop brings it back).
        int beats = 1;
        while (beats < 20 && Rng.Next() < r.AuctionBeatSkip) beats++;
        return beats * r.AuctionBeat;
    }

    void Drop(AuctionState a)
    {
        var r = CI.Pack.Rules;
        // Measured: never below the current bid and one drop (585 / 610, 825 / 850).
        a.Asking = Math.Max(a.CurrentBid + r.AuctionDrop, a.Asking - r.AuctionDrop);
        a.NextDropAt += r.AuctionTick;
        // Measured: when a drop brings the asking price back to what they pay, they bid at once (the last
        // bids came right after the drops at 12 and 16 s).
        if (a.RivalsWaiting && a.Asking <= a.RivalMax)
        {
            a.RivalsWaiting = false;
            a.NextRivalIn = Math.Min(a.NextRivalIn, Rng.Range(0.02, 0.06));
        }
    }

    void DropOpenBuyAuction()
    {
        if (State.Auction is { Mode: AuctionMode.Buy } old && State.Vehicles.TryGetValue(old.VehicleId, out var v) && v.Owner == Owner.Auction)
            State.Vehicles.Remove(old.VehicleId);
    }

    /// <summary>Go To Auction from the WorkShop. Measured: with every bay of the Car Lot taken the Auction is closed
    /// to you until you sell a car. A car you win needs a bay sooner or later, so the one in the WorkShop counts.</summary>
    public Result<AuctionState> GoToAuction()
    {
        if (State.Job is not null) return Result.Fail<AuctionState>("Finish the job first.");
        if (CarsOwned >= LotBays)
            return Result.Fail<AuctionState>("There is no room left on your Car Lot. Sell some of your cars at the Auction first.", "lot_full");
        return State.Auction is { } a ? Result.Success(a) : NextAuctionCar();
    }

    /// <summary>Go To Auction, Skip Car, or the next car after one is sold: a new car goes on the block, driving on as
    /// the one before drives off when <paramref name="driveOn"/>.</summary>
    public Result<AuctionState> NextAuctionCar(bool driveOn = false)
    {
        if (State.Job is not null) return Result.Fail<AuctionState>("Finish the job first.");
        if (State.Auction is { Mode: AuctionMode.Sell, Closed: false }) return Result.Fail<AuctionState>("Wait for your car to sell first.");
        DropOpenBuyAuction();
        var pool = CI.Pack.Cars.Where(c => c.MinSkill <= State.Skill).ToList();
        var car = Rng.Pick(pool.Count > 0 ? pool : CI.Pack.Cars);
        var r = CI.Pack.Rules;
        VehicleState v;
        if (r.AuctionCars is { Count: > 0 } kinds)
        {
            var shares = r.AuctionBeatenShare;
            bool beaten = shares is { Length: > 0 } && Rng.Next() < shares[Math.Clamp(State.Skill, 0, shares.Length - 1)];
            // Measured: the top skill's beaten-up cars are wrecks (73 to 96 % gone), Expert's less so (41 to 79 %).
            bool top = State.Skill >= r.Skills.Count - 1;
            var sameKind = kinds.Where(k => k.Beaten == beaten && (!beaten || k.Wreck == (top && kinds.Any(x => x.Wreck)))).ToList();
            var kind = Rng.Pick(sameKind.Count > 0 ? sameKind : kinds);
            v = NewVehicle(car, Owner.Auction, kind.Colours);
            Strip(v, kind.Missing);
        }
        else v = NewVehicle(car, Owner.Auction, r.AuctionWearWeights, r.AuctionMissingChance);
        State.Auction = NewAuction(AuctionMode.Buy, v, driveOn);
        Emit(new AuctionEvent(AuctionWhat.NextCar, State.Auction));
        Changed();
        return Result.Success(State.Auction);
    }

    public Result<AuctionState> SkipCar() => State.Auction is { Arriving: > 0 } a ? Result.Success(a) : NextAuctionCar(driveOn: true);

    /// <summary>Auction Car: put your car on the block. The only way to get it back is to outbid everyone.</summary>
    public Result<AuctionState> AuctionOwnCar(string vehicleId)
    {
        if (State.Job is not null) return Result.Fail<AuctionState>("Finish the job first.");
        if (!State.Vehicles.TryGetValue(vehicleId, out var v) || v.Owner != Owner.Player) return Result.Fail<AuctionState>("That is not your car.");
        if (State.Auction is { Mode: AuctionMode.Sell, Closed: false }) return Result.Fail<AuctionState>("One of your cars is already on the block.");
        DropOpenBuyAuction();
        if (State.Workshop == vehicleId) SetWorkshop(null);
        State.Lot.Remove(vehicleId);
        State.Auction = NewAuction(AuctionMode.Sell, v);
        Changed();
        return Result.Success(State.Auction);
    }

    /// <summary>Place Bid at this Asking Price. You pay when the car is yours. Measured: with the high bid already
    /// yours, it raises your own bid to the asking price all the same.</summary>
    public Result<AuctionState> PlaceBid()
    {
        if (State.Auction is not { Closed: false } a) return Result.Fail<AuctionState>("Nothing is being auctioned.");
        if (a.Arriving > 0) return Result.Fail<AuctionState>("The next car is not on the block yet.", "arriving");
        if (State.Cash < a.Asking) return Result.Fail<AuctionState>($"You need {Money(a.Asking)} for that bid.", "cash");
        Bid(a, Bidder.Player);
        Emit(new AuctionEvent(AuctionWhat.PlayerBid, a));
        Changed();
        return Result.Success(a);
    }

    void Bid(AuctionState a, Bidder who)
    {
        a.CurrentBid = a.Asking;
        a.Leader = who;
        a.History.Add(new Bid { Who = who, Amount = a.CurrentBid, At = a.Elapsed });
        a.Asking = a.CurrentBid + a.Step;
    }

    void TickAuction(double dt)
    {
        var a = State.Auction!;
        if (a.Arriving > 0)
        {
            double used = Math.Min(dt, a.Arriving);
            a.Arriving -= used;
            dt -= used;
            if (a.Arriving > 1e-9) return;
            a.Arriving = 0;
            Drop(a);
            Emit(new AuctionEvent(AuctionWhat.Drop, a));
        }
        double end = Math.Min(a.Elapsed + dt, a.Duration);
        // Walk through the drops and rival bids that fall inside this tick, in time order.
        while (a.Open)
        {
            double rivalAt = a.Elapsed + a.NextRivalIn;
            double next = Math.Min(a.NextDropAt, rivalAt);
            if (next > end) break;
            a.NextRivalIn -= next - a.Elapsed;
            a.Elapsed = next;
            if (next == a.NextDropAt)
            {
                Drop(a);
                Emit(new AuctionEvent(AuctionWhat.Drop, a));
                continue;
            }
            a.NextRivalIn = NextRivalDelay(a.Elapsed);
            // Rivals bid the asking price while it is within what they will pay (they also outbid each other).
            if (a.Asking <= a.RivalMax)
            {
                Bid(a, Bidder.Rival);
                Emit(new AuctionEvent(AuctionWhat.RivalBid, a));
            }
            a.RivalsWaiting = a.Asking > a.RivalMax;
        }
        a.NextRivalIn -= end - a.Elapsed;
        a.Elapsed = end;
        if (a.Elapsed >= a.Duration) CloseAuction(a);
        Changed();
    }

    void CloseAuction(AuctionState a)
    {
        a.Closed = true;
        State.Vehicles.TryGetValue(a.VehicleId, out var v);
        if (a.Mode == AuctionMode.Buy)
        {
            if (a.Leader == Bidder.Player && v is not null)
            {
                State.Cash -= a.CurrentBid;
                State.Stats.Spent += a.CurrentBid;
                Emit(new MoneyEvent(-a.CurrentBid, Account.Cash));
                v.Owner = Owner.Player;
                v.Stats.OrigCost = a.CurrentBid;
                v.Number = State.Stats.CarsBought++;
                if (!State.Shelves.ContainsKey(v.ModelId)) NewShelf(v.ModelId, v, won: true);
                ParkWonCar(v);
                a.Result = new AuctionResult { Buyer = Bidder.Player, Price = a.CurrentBid };
                Log($"The {CarName(v)} is yours for {Money(a.CurrentBid)}.", LogKind.Good);
                Emit(new AuctionEvent(AuctionWhat.Closed, a));
                return;
            }
            // Somebody else got it (or nobody bid): it drives off and the next car drives on.
            a.Result = new AuctionResult { Buyer = a.Leader == Bidder.Rival ? Bidder.Rival : null, Price = a.CurrentBid };
            Emit(new AuctionEvent(AuctionWhat.Closed, a));
            NextAuctionCar(driveOn: true);
            return;
        }
        if (v is null) return;
        if (a.Leader == Bidder.Rival)
        {
            var profit = a.CurrentBid - v.Stats.OrigCost - v.Stats.RepairCost;
            a.Result = new AuctionResult { Buyer = Bidder.Rival, Price = a.CurrentBid, Profit = profit };
            State.Stats.CarsSold++;
            State.Stats.CarsRepaired++;
            State.Vehicles.Remove(a.VehicleId);
            Log($"Your car sold for {Money(a.CurrentBid)}. You {(profit >= 0 ? "made" : "lost")} {Money(Math.Abs(profit))}.", profit >= 0 ? LogKind.Good : LogKind.Bad);
            Receive(a.CurrentBid, Account.Cash);
        }
        else
        {
            // You outbid everyone for your own car, or nobody wanted it. Measured: the sale is called off and all you pay
            // is a service fee, 5 % of your bid in whole $5; the car comes back as it was (its costs unchanged).
            decimal? fee = null;
            if (a.Leader == Bidder.Player)
            {
                fee = Math.Floor(a.CurrentBid * CI.Pack.Rules.AuctionBuyBackFee / 5) * 5;
                if (fee > 0)
                {
                    State.Cash -= fee.Value;
                    State.Stats.Spent += fee.Value;
                    Emit(new MoneyEvent(-fee.Value, Account.Cash));
                }
            }
            a.Result = new AuctionResult { Buyer = a.Leader == Bidder.Player ? Bidder.Player : null, Price = a.CurrentBid, Fee = fee };
            ParkWonCar(v);
            Log(fee is { } f ? $"You outbid everybody for your own car: the sale is off, and the service fee is {Money(f)}." : "Nobody bid. Your car is still yours.");
        }
        Emit(new AuctionEvent(AuctionWhat.Closed, a));
    }

    /// <summary>A car you won goes straight into the Workshop; measured: the car that was in it goes to the Car Lot.</summary>
    void ParkWonCar(VehicleState v)
    {
        if (State.Job is not null)
        {
            ParkInLot(v.Id);
            return;
        }
        if (State.Workshop is { } there && there != v.Id) ParkInLot(there);
        SetWorkshop(v.Id);
    }

    /// <summary>Go Back To WorkShop from the Auction.</summary>
    public Result<Unit> LeaveAuction()
    {
        var a = State.Auction;
        if (a is { Closed: false, Mode: AuctionMode.Sell }) return Result.Fail("Your car is on the block. Wait for the hammer.");
        DropOpenBuyAuction();
        State.Auction = null;
        Changed();
        return Result.Done();
    }

    public decimal CarValue(string vehicleId) => Economy.CarValue(CI, Vehicle(vehicleId));
}
