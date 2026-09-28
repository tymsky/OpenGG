namespace OpenGG.Core.Content;

/// <summary>Fast lookups over a <see cref="ContentPack"/>.</summary>
public sealed class ContentIndex
{
    public ContentPack Pack { get; }

    readonly Dictionary<string, PartDef> parts = [];
    readonly Dictionary<string, CarModelDef> cars = [];
    readonly Dictionary<string, Dictionary<string, SlotDef>> slots = [];
    readonly Dictionary<string, Dictionary<string, List<SlotDef>>> childMap = [];
    readonly Dictionary<string, ToolDef> tools = [];
    readonly Dictionary<string, ToolDef> kindTool = [];
    readonly Dictionary<string, FastenerKindDef> kinds = [];
    readonly Dictionary<string, List<PartDef>> bySlotType = [];

    public ContentIndex(ContentPack pack)
    {
        Pack = pack;
        foreach (var p in pack.Parts)
        {
            parts[p.Id] = p;
            if (!bySlotType.TryGetValue(p.SlotType, out var list)) bySlotType[p.SlotType] = list = [];
            list.Add(p);
        }
        foreach (var c in pack.Cars)
        {
            cars[c.Id] = c;
            var sm = new Dictionary<string, SlotDef>();
            var cm = new Dictionary<string, List<SlotDef>>();
            foreach (var s in c.Slots)
            {
                sm[s.Id] = s;
                foreach (var parent in s.Parents)
                {
                    if (!cm.TryGetValue(parent, out var l)) cm[parent] = l = [];
                    l.Add(s);
                }
            }
            slots[c.Id] = sm;
            childMap[c.Id] = cm;
        }
        foreach (var t in pack.Tools)
        {
            tools[t.Id] = t;
            foreach (var k in t.FastenerKinds) kindTool.TryAdd(k, t);
        }
        foreach (var k in pack.FastenerKinds) kinds[k.Id] = k;
    }

    public PartDef Part(string id) => parts.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"unknown part {id}");
    public bool HasPart(string id) => parts.ContainsKey(id);
    public bool TryPart(string id, out PartDef part) => parts.TryGetValue(id, out part!);
    public CarModelDef Car(string id) => cars.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"unknown car {id}");
    public bool HasCar(string id) => cars.ContainsKey(id);

    public SlotDef Slot(string carId, string slotId) =>
        slots.TryGetValue(carId, out var m) && m.TryGetValue(slotId, out var s) ? s : throw new KeyNotFoundException($"unknown slot {carId}/{slotId}");

    public bool HasSlot(string carId, string slotId) => slots.TryGetValue(carId, out var m) && m.ContainsKey(slotId);

    /// <summary>Slots mounted on this one.</summary>
    public IReadOnlyList<SlotDef> Children(string carId, string slotId) =>
        childMap.TryGetValue(carId, out var m) && m.TryGetValue(slotId, out var l) ? l : [];

    public ToolDef Tool(string id) => tools.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"unknown tool {id}");
    public ToolDef? ToolForKind(string kind) => kindTool.GetValueOrDefault(kind);
    public FastenerKindDef Kind(string id) => kinds.TryGetValue(id, out var k) ? k : throw new KeyNotFoundException($"unknown fastener kind {id}");
    public IReadOnlyList<PartDef> PartsFor(string slotType) => bySlotType.TryGetValue(slotType, out var l) ? l : [];

    /// <summary>Car ids that have a slot accepting this part.</summary>
    public IEnumerable<string> CarsFitting(PartDef part) =>
        Pack.Cars.Where(c => c.Slots.Any(s => s.SlotType == part.SlotType)).Select(c => c.Id);
}
