namespace OpenGG.Core.Content;

public static class Validation
{
    /// <summary>Checks internal references. An empty list means the pack is consistent.</summary>
    public static List<string> Validate(ContentPack pack)
    {
        var errors = new List<string>();
        var partIds = pack.Parts.Select(p => p.Id).ToHashSet();
        var partById = pack.Parts.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var kindIds = pack.FastenerKinds.Select(k => k.Id).ToHashSet();
        var toolKinds = pack.Tools.SelectMany(t => t.FastenerKinds).ToHashSet();

        foreach (var dup in pack.Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1)) errors.Add($"duplicate part {dup.Key}");
        foreach (var p in pack.Parts)
            foreach (var f in p.Fasteners)
            {
                if (!kindIds.Contains(f.Kind)) errors.Add($"part {p.Id}: unknown fastener kind {f.Kind}");
                if (!toolKinds.Contains(f.Kind)) errors.Add($"part {p.Id}: no tool handles {f.Kind}");
            }

        var skills = pack.Rules.Skills;
        if (skills.Count == 0) errors.Add("rules: no skill levels");
        for (int i = 1; i < skills.Count; i++)
            if (skills[i].Cash < skills[i - 1].Cash) errors.Add($"skill {skills[i].Name}: thresholds must increase");

        foreach (var c in pack.Cars)
        {
            if (c.MinSkill >= Math.Max(1, skills.Count)) errors.Add($"car {c.Id}: minSkill {c.MinSkill} has no skill level");
            var ids = new HashSet<string>();
            foreach (var s in c.Slots)
                if (!ids.Add(s.Id)) errors.Add($"car {c.Id}: duplicate slot {s.Id}");
            var byId = c.Slots.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var s in c.Slots)
            {
                foreach (var p in s.Parents)
                    if (!ids.Contains(p)) errors.Add($"car {c.Id}: slot {s.Id} has unknown parent {p}");
                foreach (var b in s.BlockedBy)
                    if (!ids.Contains(b)) errors.Add($"car {c.Id}: slot {s.Id} blocked by unknown {b}");
                if (s.DefaultPart is { } dp)
                {
                    if (!partById.TryGetValue(dp, out var part)) errors.Add($"car {c.Id}: slot {s.Id} default part {dp} missing");
                    else if (part.SlotType != s.SlotType) errors.Add($"car {c.Id}: slot {s.Id} ({s.SlotType}) default part {dp} is {part.SlotType}");
                }
                else if (s.Required) errors.Add($"car {c.Id}: required slot {s.Id} has no default part");
            }
            // Mounting must not loop.
            var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
            bool Cycle(string id)
            {
                if (state.TryGetValue(id, out int st)) return st == 1;
                state[id] = 1;
                if (byId.TryGetValue(id, out var s))
                    foreach (var p in s.Parents)
                        if (Cycle(p)) return true;
                state[id] = 2;
                return false;
            }
            foreach (var s in c.Slots)
                if (Cycle(s.Id)) { errors.Add($"car {c.Id}: parent cycle at {s.Id}"); break; }
        }

        var fams = pack.Cars.SelectMany(c => c.Slots.Select(s => s.Family)).ToHashSet();
        foreach (var j in pack.Jobs)
            foreach (var r in j.Requirements)
            {
                foreach (var fam in r.Families)
                    if (!fams.Contains(fam)) errors.Add($"job {j.Id}: family {fam} not used by any car");
                foreach (var p in r.Parts ?? [])
                    if (!partIds.Contains(p)) errors.Add($"job {j.Id}: unknown part {p}");
            }

        var d = pack.Rules.Diagnosis;
        foreach (var fam in d.Dead.Concat(d.NoCrank).Concat(d.NoStart).Concat(d.Rough).Concat(d.Loud))
            if (!fams.Contains(fam)) errors.Add($"diagnosis: family {fam} not used by any car");
        return errors;
    }
}
