// Assembly rules ("bolt-em up"). Pure functions over content + vehicle state.
//
//   - A bolt can be reached when its part is installed, every blocker slot is empty
//     (or open, for hinged parts) and, for hinged parts, the part itself is open.
//   - A part can come off when all its bolts are out, nothing is mounted on it
//     and it can be reached.
//   - A part can go on when the slot is empty, the part fits, every part it is mounted on
//     is in place and the slot can be reached. It goes on loose: every bolt must then be tightened.

using OpenGG.Core.Content;

namespace OpenGG.Core.Sim;

/// <summary>The answer to "can I do this?". On failure, <see cref="Slots"/> names the parts in the way; <see cref="Names"/>,
/// when given, names them where the slots would not (a part an alternative stands in for).</summary>
public sealed record Check(bool Ok, string Code = "", string Msg = "", IReadOnlyList<string>? Slots = null, IReadOnlyList<string>? Names = null)
{
    public static readonly Check Pass = new(true);
    public static Check Fail(string code, string msg, IReadOnlyList<string>? slots = null, IReadOnlyList<string>? names = null) => new(false, code, msg, slots, names);
}

public sealed record Completeness(IReadOnlyList<string> Missing, IReadOnlyList<string> Loose)
{
    public bool Done => Missing.Count == 0 && Loose.Count == 0;
}

public static class VehicleRules
{
    public static SlotState SlotStateOf(VehicleState v, string slotId) =>
        v.Slots.TryGetValue(slotId, out var s) ? s : throw new KeyNotFoundException($"vehicle {v.Id} has no slot {slotId}");

    /// <summary>Blocker slots that currently stand in the way.</summary>
    public static List<string> ActiveBlockers(ContentIndex ci, VehicleState v, SlotDef slot)
    {
        var outList = new List<string>();
        foreach (var b in slot.BlockedBy)
        {
            if (!v.Slots.TryGetValue(b, out var st) || st.Part is null) continue;
            if (ci.Slot(v.ModelId, b).Openable is not null && st.Open) continue;
            outList.Add(b);
        }
        return outList;
    }

    static string Names(ContentIndex ci, VehicleState v, IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(id => ci.Slot(v.ModelId, id).Name));

    /// <summary>Can the part in this slot be reached at all?</summary>
    public static Check Reach(ContentIndex ci, VehicleState v, string slotId)
    {
        var slot = ci.Slot(v.ModelId, slotId);
        var st = SlotStateOf(v, slotId);
        if (st.Part is null) return Check.Fail("empty", $"{slot.Name}: nothing installed.");
        var blockers = ActiveBlockers(ci, v, slot);
        if (blockers.Count > 0)
        {
            var openable = blockers.Where(b => ci.Slot(v.ModelId, b).Openable is not null).ToList();
            if (openable.Count == blockers.Count)
                return Check.Fail("closed", $"Open the {Names(ci, v, openable).ToLowerInvariant()} first.", blockers);
            return Check.Fail("blocked", $"You need to remove the {Names(ci, v, blockers)} first.", blockers);
        }
        return Check.Pass;
    }

    public static Check FastenerCheck(ContentIndex ci, VehicleState v, string slotId, int index, string toolId)
    {
        var slot = ci.Slot(v.ModelId, slotId);
        var st = SlotStateOf(v, slotId);
        if (st.Part is null) return Check.Fail("empty", $"{slot.Name}: nothing installed.");
        var part = ci.Part(st.Part.PartId);
        if (index < 0 || index >= part.Fasteners.Count) return Check.Fail("bad_index", "No such bolt.");
        var f = part.Fasteners[index];
        var tool = ci.Tool(toolId);
        if (!tool.FastenerKinds.Contains(f.Kind))
        {
            var right = ci.ToolForKind(f.Kind);
            return Check.Fail("wrong_tool", $"That {ci.Kind(f.Kind).Name.ToLowerInvariant()} needs the {right?.Name ?? "right tool"}.");
        }
        var r = Reach(ci, v, slotId);
        if (!r.Ok) return r;
        if (slot.Openable is not null && !st.Open) return Check.Fail("closed", $"Open the {slot.Name.ToLowerInvariant()} first.");
        return Check.Pass;
    }

    /// <summary>
    /// Installed parts that are mounted on this slot: through the slot's parents, or through the
    /// installed part's own mounts. A part that may hang on any one of several mounts only counts
    /// when this is the last of them still in place.
    /// </summary>
    public static List<string> MountedChildren(ContentIndex ci, VehicleState v, string slotId)
    {
        bool Filled(string id) => v.Slots.TryGetValue(id, out var s) && s.Part is not null;
        var list = new List<string>();
        foreach (var s in ci.Car(v.ModelId).Slots)
        {
            if (s.Id == slotId || v.Slots[s.Id].Part is not { } p) continue;
            if (s.Parents.Contains(slotId)) list.Add(s.Id);
            else if (ci.Part(p.PartId) is { Mounts: { } mounts } def && mounts.Contains(slotId)
                     && (!def.MountsAny || !mounts.Any(m => m != slotId && Filled(m))))
                list.Add(s.Id);
        }
        return list;
    }

    public static Check RemoveCheck(ContentIndex ci, VehicleState v, string slotId)
    {
        var slot = ci.Slot(v.ModelId, slotId);
        var st = SlotStateOf(v, slotId);
        if (st.Part is null) return Check.Fail("empty", $"{slot.Name}: nothing installed.");
        var r = Reach(ci, v, slotId);
        if (!r.Ok) return r;
        var kids = MountedChildren(ci, v, slotId);
        if (kids.Count > 0) return Check.Fail("children", $"You need to remove the {Names(ci, v, kids)} first.", kids);
        if (ci.Part(st.Part.PartId).RemoveAfter is { Count: > 0 } after)
        {
            var first = after.Where(id => v.Slots.TryGetValue(id, out var s) && s.Part is not null).ToList();
            if (first.Count > 0) return Check.Fail("children", $"You need to remove the {Names(ci, v, first)} first.", first);
        }
        int tight = st.Fasteners.Count(f => f);
        if (tight > 0) return Check.Fail("fastened", $"The {slot.Name} is still held by {tight} bolt{(tight > 1 ? "s" : "")}.");
        return Check.Pass;
    }

    /// <summary>Measured: a black part let go over the car gets "Assembly Error!" and stays in the Parts Bin (it can
    /// only be scrapped), wherever it would go.</summary>
    public static Check PartCheck(PartInstance part) =>
        part.Condition <= Condition.Black ? Check.Fail("worn_out", "That part is too far gone to go back on. Scrap it instead.") : Check.Pass;

    public static Check InstallCheck(ContentIndex ci, VehicleState v, string slotId, string partId)
    {
        var slot = ci.Slot(v.ModelId, slotId);
        var st = SlotStateOf(v, slotId);
        var part = ci.Part(partId);
        if (st.Part is not null) return Check.Fail("occupied", $"There is already a {slot.Name} on the car.", [slotId]);
        if (part.SlotType != slot.SlotType) return Check.Fail("no_fit", $"The {part.Name} doesn't fit here.");
        bool Filled(string id) => v.Slots.TryGetValue(id, out var ps) && ps.Part is not null;
        var missingParents = slot.Parents.Concat(part.MountsAny ? [] : part.Mounts ?? []).Distinct().Where(p => !Filled(p)).ToList();
        if (part.MountsAny && part.Mounts is { Count: > 0 } any && !any.Any(Filled)) missingParents.Add(any[0]);
        if (missingParents.Count > 0)
            return Check.Fail("no_parent", $"Put the {Names(ci, v, missingParents)} on first.", missingParents);
        var replaced = ReplacedParents(ci, v, slot, part);
        if (replaced.Count > 0)
        {
            var names = replaced.Select(r => ci.Part(r.Part).Name).ToList();
            return Check.Fail("no_parent", $"Put the {string.Join(", ", names)} on first.", replaced.Select(r => r.Slot).ToList(), names);
        }
        var blockers = ActiveBlockers(ci, v, slot);
        if (blockers.Count > 0) return Check.Fail("blocked", $"You need to remove the {Names(ci, v, blockers)} first.", blockers);
        return Check.Pass;
    }

    /// <summary>Empty slots the given part could go into (right now, or at all with <paramref name="ignoreAccess"/>).</summary>
    public static List<string> FittingSlots(ContentIndex ci, VehicleState v, string partId, bool ignoreAccess = false)
    {
        var part = ci.Part(partId);
        return ci.Car(v.ModelId).Slots
            .Where(s => s.SlotType == part.SlotType && v.Slots[s.Id].Part is null)
            .Where(s => ignoreAccess || InstallCheck(ci, v, s.Id, partId).Ok)
            .Select(s => s.Id)
            .ToList();
    }

    /// <summary>The <see cref="PartDef.NeedsParts"/> of a part that an alternative stands in for on the car, with the slots
    /// they go in.</summary>
    static List<(string Slot, string Part)> ReplacedParents(ContentIndex ci, VehicleState v, SlotDef slot, PartDef part)
    {
        var list = new List<(string, string)>();
        if (part.NeedsParts is not { } needs) return list;
        var under = slot.Parents.Concat(part.Mounts ?? []).ToHashSet();
        var slots = ci.Car(v.ModelId).Slots;
        foreach (var id in needs)
        {
            var type = ci.Part(id).SlotType;
            if (slots.Find(s => s.SlotType == type && under.Contains(s.Id)) is { } at && v.Slots[at.Id].Part is { } on && on.PartId != id)
                list.Add((at.Id, id));
        }
        return list;
    }

    /// <summary>
    /// Empty stock places the car can't have filled as it is: their part goes on a part an alternative has replaced, or
    /// on such a place. They are not missing. Measured: the Escort's Cosworth job was done without the Back Windshield
    /// (it goes on the Trunk, the Hatchback Cosworth in its place), its Convertible job ASSEMBLED without the Trunk and
    /// either windshield (they go on the Cab Roof).
    /// </summary>
    public static HashSet<string> Unfillable(ContentIndex ci, VehicleState v)
    {
        var result = new HashSet<string>();
        var car = ci.Car(v.ModelId);
        for (bool more = true; more;)
        {
            more = false;
            foreach (var s in car.Slots)
            {
                if (s.DefaultPart is not { } d || v.Slots[s.Id].Part is not null || result.Contains(s.Id)) continue;
                var part = ci.Part(d);
                bool cut = s.Parents.Concat(part.MountsAny ? [] : part.Mounts ?? []).Any(result.Contains)
                    || part.MountsAny && part.Mounts is { Count: > 0 } any && any.All(result.Contains)
                    || ReplacedParents(ci, v, s, part).Count > 0;
                if (!cut) continue;
                result.Add(s.Id);
                more = true;
            }
        }
        return result;
    }

    public static Completeness GetCompleteness(ContentIndex ci, VehicleState v, Region? region = null)
    {
        var missing = new List<string>();
        var loose = new List<string>();
        HashSet<string>? unfillable = null;
        foreach (var s in ci.Car(v.ModelId).Slots)
        {
            if (region is { } r && s.Region != r) continue;
            var st = v.Slots[s.Id];
            if (st.Part is null)
            {
                if (s.Required && !(unfillable ??= Unfillable(ci, v)).Contains(s.Id)) missing.Add(s.Id);
                continue;
            }
            if (st.Fasteners.Any(f => !f)) loose.Add(s.Id);
        }
        return new Completeness(missing, loose);
    }

    public static bool IsComplete(ContentIndex ci, VehicleState v, Region? region = null) =>
        GetCompleteness(ci, v, region).Done;

    /// <summary>
    /// The ASSEMBLED tag: shown only when nothing is missing (or loose) in the section, coloured by the
    /// worst part in it. Null = no tag. The car is mint when the COMPLETE tag is green. A part being unscrewed
    /// (<paramref name="unscrewing"/>) is on until its last bolt is out (measured: the red tag stayed up with a
    /// wheel's bolt out; with a part being bolted on there was none).
    /// </summary>
    public static int? AssembledCondition(ContentIndex ci, VehicleState v, Region? region = null, string? unscrewing = null)
    {
        var c = GetCompleteness(ci, v, region);
        if (c.Missing.Count > 0 || c.Loose.Any(id => id != unscrewing)) return null;
        int worst = Condition.Green;
        foreach (var s in ci.Car(v.ModelId).Slots)
        {
            if (region is { } reg && s.Region != reg) continue;
            if (v.Slots[s.Id].Part is { } p) worst = Math.Min(worst, p.Condition);
        }
        return worst;
    }

    /// <summary>
    /// Slots that have to come off before <paramref name="slotIds"/> can be removed (including the slots
    /// themselves). Hinged blockers only need opening, so they are not included.
    /// </summary>
    public static HashSet<string> RemovalClosure(ContentIndex ci, string carId, IReadOnlyCollection<string> slotIds)
    {
        var result = new HashSet<string>();
        void Visit(string id)
        {
            if (result.Contains(id)) return;
            var s = ci.Slot(carId, id);
            if (s.Openable is not null && !slotIds.Contains(id)) return;
            result.Add(id);
            foreach (var c in ci.Children(carId, id)) Visit(c.Id);
            foreach (var b in s.BlockedBy) Visit(b);
        }
        foreach (var id in slotIds) Visit(id);
        return result;
    }

    public static SlotState EmptySlot() => new();

    public static SlotState FilledSlot(ContentIndex ci, PartInstance part, bool tight)
    {
        int n = ci.Part(part.PartId).Fasteners.Count;
        return new SlotState { Part = part, Fasteners = Enumerable.Repeat(tight, n).ToList() };
    }
}
