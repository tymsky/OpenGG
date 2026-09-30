// A game's state written out plainly, for comparing two games (OpenGG's after a scripted run, and the original's
// brought over from its save by MekImport): ids, the log and anything the original's saves don't keep are left out.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenGG.Core.Content;

namespace OpenGG.Core.Sim;

public static class StateDump
{
    static readonly string[] Colours = ["black", "red", "yellow", "green"];

    /// <summary>The state as JSON: the mechanic's figures, the cars (the WorkShop's, then the Car Lot's in its order) with
    /// their parts by place, the Parts Bin and the JunkYard shelves by car model, the decals' uses.</summary>
    public static string Of(GameState s, ContentIndex ci)
    {
        var root = new JsonObject
        {
            ["mechanic"] = s.Mechanic,
            ["cash"] = Money(s.Cash),
            ["skill"] = s.Skill,
            ["playTime"] = Math.Round(s.PlayTime),
            ["freePlay"] = s.FreePlay,
            ["jobsDone"] = new JsonArray(s.CompletedJobs.Order(StringComparer.Ordinal).Select(j => (JsonNode)j).ToArray()),
            ["carsBought"] = s.Stats.CarsBought,
            ["workshop"] = s.Workshop is { } w && s.Vehicles.TryGetValue(w, out var wv) ? Car(wv, ci) : null,
            ["lot"] = new JsonArray(s.Lot.Where(s.Vehicles.ContainsKey).Select(id => (JsonNode)Car(s.Vehicles[id], ci)).ToArray()),
            ["bin"] = ByModel(s.Bin.Select(b => b.Part), ci),
            ["shelves"] = Shelves(s, ci),
            ["decals"] = Sorted(s.DecalUses.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => (JsonNode)kv.Value)),
        };
        if (s.Job is { } job)
            root["job"] = new JsonObject { ["fee"] = Money(job.Fee), ["budget"] = Money(job.Budget), ["template"] = job.TemplateId };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    static JsonObject Car(VehicleState v, ContentIndex ci)
    {
        var parts = new Dictionary<string, JsonNode?>();
        foreach (var (slot, st) in v.Slots)
            parts[slot] = st.Part is { } p ? Part(p, ci, st.Fasteners.Count > 0 && st.Fasteners.Any(f => !f) ? $"{st.Fasteners.Count(f => f)}/{st.Fasteners.Count}" : null) : "missing";
        return new JsonObject
        {
            ["model"] = v.ModelId,
            ["number"] = v.Number,
            ["origCost"] = Money(v.Stats.OrigCost),
            ["repairCost"] = Money(v.Stats.RepairCost),
            ["repairTime"] = Math.Round(v.Stats.RepairTime),
            ["completed"] = v.Completed,
            ["parts"] = Sorted(parts),
        };
    }

    /// <summary>A part: its id, colour and place in the colour ("green +0.059"); bolts only when some are out.</summary>
    static JsonNode Part(PartInstance p, ContentIndex ci, string? bolts = null)
    {
        var text = $"{p.PartId} {Colours[Math.Clamp(p.Condition, 0, 3)]} {p.Extra.ToString("+0.000;-0.000", CultureInfo.InvariantCulture)}";
        if (ci.HasPart(p.PartId)) text += $" ({ci.Part(p.PartId).Name})";
        return bolts is null ? text : $"{text} bolts {bolts}";
    }

    static JsonObject ByModel(IEnumerable<PartInstance> parts, ContentIndex ci) =>
        Sorted(parts.GroupBy(p => Model(p.PartId)).ToDictionary(g => g.Key,
            g => (JsonNode?)new JsonArray(g.Select(p => Part(p, ci)).ToArray())));

    static JsonObject Shelves(GameState s, ContentIndex ci) =>
        Sorted(s.Shelves.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key,
            kv => (JsonNode?)new JsonArray(kv.Value.Select(j => Part(j.Part, ci)).ToArray())));

    /// <summary>The car model a part belongs to (the original's parts are "model.number").</summary>
    static string Model(string partId) => partId.LastIndexOf('.') is int i and > 0 ? partId[..i] : partId;

    static JsonObject Sorted(Dictionary<string, JsonNode?> d)
    {
        var o = new JsonObject();
        foreach (var k in d.Keys.Order(StringComparer.Ordinal)) o[k] = d[k];
        return o;
    }

    static JsonNode Money(decimal m) => JsonValue.Create(Math.Round(m, 2).ToString("0.00", CultureInfo.InvariantCulture))!;
}
