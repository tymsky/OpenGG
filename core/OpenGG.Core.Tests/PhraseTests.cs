using OpenGG.Core.Content;
using OpenGG.Core.Original;
using OpenGG.Core.Sim;
using static OpenGG.Core.Tests.TestUtil;

namespace OpenGG.Core.Tests;

/// <summary>Random jobs written from a phrase book (our own words here, in the original's format).</summary>
public class PhraseTests
{
    const string Sample = """
        ; variables
        %Pal = Random: "pal", "friend"
        %Bad = CondRange: "slightly", "quite", "badly"
        %Some = PercRange: "a part or two", "some", "most", "all"
        %Where = RegionName: "whole car", "engine", "body", "running gear"
        %Hurt = Random: "damaged"

        Symptom: Engine: "%Pal, the engine coughs."
        Smell: Engine: "an unknown kind is skipped."
        Cause: Body: "a shopping cart rolled into it."
        RegionPerc: "%Some %Where parts are %Bad %Hurt."
        PartDam: "the %PartName is %Bad %Hurt."
        PartGone: "the %PartName fell off."

        Face: Happy: "Ann-h.jpg"
        Face: Sad: "ann-s.jpg"
        PreReq: "sorry to bother you, but "
        PostReq: "can you help?"
        Thank: "thanks, %Pal!"

        Face: Happy: "bo-h.jpg"
        Face: Sad: "bo-s.jpg"
        PreReq: "hi."
        PostReq: "fix it?"
        Thank: "great."
        """;

    static JobPhraseBook Book(List<string>? problems = null) => PhraseFile.Parse(Sample, f => "face:" + f, problems);

    [Fact]
    public void ReadsVariablesSentencesAndCustomers()
    {
        var problems = new List<string>();
        var b = Book(problems);
        Assert.Equal(5, b.Variables.Count);
        Assert.Equal(PhraseChoice.Condition, b.Variables["Bad"].Choice);
        Assert.Equal(PhraseChoice.Share, b.Variables["Some"].Choice);
        Assert.Equal(["pal", "friend"], b.Variables["Pal"].Words);
        Assert.Equal(5, b.Sentences.Count);
        Assert.Contains(b.Sentences, s => s is { Kind: PhraseKind.Symptom, Region: View.Engine });
        Assert.Contains(b.Sentences, s => s is { Kind: PhraseKind.Cause, Region: View.Body });
        Assert.Single(problems);
        Assert.Equal(2, b.Customers.Count);
        Assert.Equal("face:ann-s.jpg", b.Customers[0].Face);
        Assert.Equal("face:Ann-h.jpg", b.Customers[0].HappyFace);
        Assert.Equal("sorry to bother you, but ", b.Customers[0].Before);
        Assert.Equal("great.", b.Customers[1].Thanks);
    }

    [Fact]
    public void PicksWordsByTheDamage()
    {
        var w = new PhraseWriter(Book(), _ => 0);
        const string text = "%Some %Where parts are %Bad %Hurt.";
        Assert.Equal("a part or two engine parts are slightly damaged.", w.Expand(text, new PhraseTopic(View.Engine, 0, 0.05, 2)));
        // "How many" goes by quarters of the region, however many parts that is.
        Assert.Equal("a part or two body parts are quite damaged.", w.Expand(text, new PhraseTopic(View.Body, 0.5, 0.2, 3)));
        Assert.Equal("some body parts are quite damaged.", w.Expand(text, new PhraseTopic(View.Body, 0.5, 0.3, 6)));
        Assert.Equal("most body parts are quite damaged.", w.Expand(text, new PhraseTopic(View.Body, 0.5, 0.6, 12)));
        Assert.Equal("all whole car parts are badly damaged.", w.Expand(text, new PhraseTopic(View.Complete, 1, 1, 40)));
        Assert.Equal("the Radiator is badly damaged.", w.Expand("the %PartName is %Bad %Hurt.", new PhraseTopic(View.Engine, 1, 0.1, 1, "Radiator")));
        Assert.Equal("%Nobody here", w.Expand("%Nobody here", new PhraseTopic(View.Engine, 0, 0, 1)));
    }

    [Fact]
    public void RunsOnAfterButAndStartsSentencesWithCapitals()
    {
        Assert.Equal("Sorry to bother you, but the hood fell off. Can you help?",
            PhraseWriter.Join(["sorry to bother you, but ", "the hood fell off.", "can you help?"]));
        Assert.Equal("Hi. The engine coughs! Fix it?", PhraseWriter.Join(["hi.", "the engine coughs!", "fix it?"]));
    }

    [Fact]
    public void ALowerCaseSentenceInsideAPieceGetsACapitalAndTwoSpaces()
    {
        // The spacing measured in the original's Job Requests (our own words).
        Assert.Equal("Hey?  Are you there? The brakes squeal!  And the doors rattle. If you're open, can you look?",
            PhraseWriter.Join(["hey? are you there?", "the brakes squeal! and the doors rattle.", "if you're open, can you look?"]));
        // A sentence that already starts with a capital keeps one space; only first letters change.
        Assert.Equal("The fan is loud.  Also, I hit a cone.  Plus, i lost a hubcap.",
            PhraseWriter.Join(["the fan is loud. also, I hit a cone. plus, i lost a hubcap."]));
        Assert.Equal("Plus, the mirror fell off! I need a new one.", PhraseWriter.Piece("plus, the mirror fell off! I need a new one."));
        // Job Update: single spaces.
        Assert.Equal("The fan is loud. And my seat squeaks.", PhraseWriter.Hint("the fan is loud. and my seat squeaks."));
    }

    /// <summary>The ai pack with only phrased random jobs.</summary>
    static ContentIndex PhrasedPack()
    {
        var a = Ai.Pack;
        var pack = new ContentPack
        {
            Id = a.Id, Name = a.Name, Tools = a.Tools, FastenerKinds = a.FastenerKinds, Parts = a.Parts, Cars = a.Cars,
            Names = a.Names, Portraits = a.Portraits, Decals = a.Decals, Paints = a.Paints, Rules = a.Rules,
            Jobs = a.Jobs.Where(j => j.Sequence is not null || j.CarId is not null).ToList(),
            Phrases = Book(),
        };
        foreach (var d in new[] { Difficulty.Easy, Difficulty.Medium })
            pack.Jobs.Add(new JobTemplateDef { Id = $"phrased.{d}", Difficulty = d, Phrased = true });
        return new ContentIndex(pack);
    }

    [Fact]
    public void FreePlayWritesRandomJobsFromThePhraseBook()
    {
        var ci = PhrasedPack();
        int seen = 0, openings = 0, closings = 0, two = 0;
        for (uint seed = 1; seed <= 60; seed++)
        {
            var g = Game.Create(ci, "Test", seed);
            g.State.FreePlay = true;
            g.State.Skill = 1;
            var r = g.RequestJob();
            Assert.True(r.Ok, r.Msg);
            var job = r.Data!;
            if (!job.TemplateId.StartsWith("phrased.")) continue;
            seen++;
            Assert.StartsWith("face:", job.Portrait);
            Assert.StartsWith("face:", job.ThanksPortrait);
            if (System.Text.RegularExpressions.Regex.IsMatch(job.Text, "^(Sorry to bother you, but |Hi\\. )")) openings++;
            if (System.Text.RegularExpressions.Regex.IsMatch(job.Text, "(Can you help\\?|Fix it\\?)$")) closings++;
            if (job.Reqs.Count == 2) two++;
            Assert.InRange(job.Reqs.Count, 0, 2);
            var v = g.Vehicle(job.VehicleId);
            // Measured: the fee is 1.25 × what it takes to put the car right, each repair priced with an extra of its own.
            var (lo, hi) = FeeRange(ci, v, job.Reqs.SelectMany(q => q.SlotIds));
            Assert.InRange(job.Fee, lo, hi);
            Assert.Equal(job.Reqs.Count == 0, job.Fee == 0);
            if (job.Reqs.Count > 0) Assert.DoesNotContain("  ", job.Nag);
            var named = new HashSet<string>();
            foreach (var q in job.Reqs)
                foreach (var id in q.SlotIds)
                {
                    named.Add(id);
                    if (q.Type == JobReqType.Missing) Assert.Null(v.Slots[id].Part);
                    else Assert.True(v.Slots[id].Part!.Condition < Condition.Green, $"{id} is not damaged");
                }
            // What the customer doesn't mention is fine.
            Assert.All(v.Slots.Where(kv => !named.Contains(kv.Key) && kv.Value.Part is not null),
                kv => Assert.Equal(Condition.Green, kv.Value.Part!.Condition));
        }
        Assert.True(seen >= 10, $"only {seen} phrased jobs");
        // About half of the customers say something first, about half something last; a third have two troubles.
        Assert.InRange(openings, seen / 5, seen * 4 / 5);
        Assert.InRange(closings, seen / 5, seen * 4 / 5);
        Assert.InRange(two, 1, seen * 2 / 3);
    }

    /// <summary>What a random job's fee can be: 1.25 × the fix cost with the parts' extras anywhere in their range.</summary>
    static (decimal Lo, decimal Hi) FeeRange(ContentIndex ci, VehicleState v, IEnumerable<string> slotIds)
    {
        var ids = slotIds.ToList();
        decimal Priced(double extra)
        {
            var p = new VehicleState { ModelId = v.ModelId };
            foreach (var id in ids)
                p.Slots[id] = v.Slots[id].Part is { } part
                    ? new SlotState { Part = new PartInstance { PartId = part.PartId, Condition = part.Condition, Extra = extra } }
                    : new SlotState();
            return Economy.FixCost(ci, p, ids);
        }
        var spread = ci.Pack.Rules.ConditionSpread;
        return (Economy.Cents(Economy.RandomJobFeeFactor * Priced(spread)) - 0.01m, Economy.Cents(Economy.RandomJobFeeFactor * Priced(-spread)) + 0.01m);
    }

    [Fact]
    public void RandomJobsComeInTheMeasuredKinds()
    {
        // Measured on the original's offers: nothing, the whole car, one region, two regions (told engine, body,
        // running gear), one or two parts; a part named in lower case, with the strongest words when it is black.
        var ci = PhrasedPack();
        var seen = new HashSet<string>();
        for (uint seed = 1; seed <= 400; seed++)
        {
            var g = Game.Create(ci, "Test", seed);
            g.State.FreePlay = true;
            g.State.Skill = 1;
            var job = g.RequestJob().Data!;
            if (!job.TemplateId.StartsWith("phrased.")) continue;
            var v = g.Vehicle(job.VehicleId);
            var car = ci.Car(v.ModelId);
            var regions = job.Reqs.Select(q => q.SlotIds.Select(id => ci.Slot(v.ModelId, id).Region).Distinct().ToList()).ToList();
            string kind = job.Reqs.Count switch
            {
                0 => "nothing",
                1 when regions[0].Count == 3 => "whole",
                1 when job.Reqs[0].SlotIds.Count == 1 && !job.Text.Contains(" parts") => "part",
                1 => "region",
                _ when job.Reqs.All(q => q.SlotIds.Count == 1) && !job.Text.Contains(" parts") => "parts",
                _ => "regions",
            };
            seen.Add(kind);
            if (kind == "regions")
            {
                Assert.All(regions, r => Assert.Single(r));
                Assert.True(regions[0][0] < regions[1][0], $"regions told out of order: {job.Text}");
            }
            if (kind is "part" or "parts")
                foreach (var q in job.Reqs)
                {
                    var name = ci.Part(ci.Slot(v.ModelId, q.SlotIds[0]).DefaultPart!).Name;
                    Assert.Contains(name.ToLowerInvariant(), job.Text);
                    var said = job.Text.ToLowerInvariant();
                    int at = said.IndexOf($"the {name.ToLowerInvariant()} is ", StringComparison.Ordinal);
                    if (q.Type == JobReqType.Fix && at >= 0)
                        Assert.StartsWith(v.Slots[q.SlotIds[0]].Part!.Condition == Condition.Black ? "badly" : "slightly",
                            said[(at + $"the {name.ToLowerInvariant()} is ".Length)..]);
                }
            if (kind == "nothing") Assert.Equal(0m, job.Fee);
        }
        Assert.Superset(new HashSet<string> { "nothing", "whole", "region", "regions", "part", "parts" }, seen);
    }

    [Fact]
    public void APhrasedJobEndsWhenEveryPartNamedIsBackAndGreen()
    {
        var ci = PhrasedPack();
        for (uint seed = 1; seed <= 40; seed++)
        {
            var g = Game.Create(ci, "Test", seed);
            g.State.FreePlay = true;
            var offer = g.RequestJob().Data!;
            if (!offer.TemplateId.StartsWith("phrased.")) continue;
            Assert.True(g.AcceptJob().Ok);
            var v = g.WorkshopVehicle()!;
            // An order the whole car comes apart in (missing parts go back in their place in it).
            var probe = Game.Create(ci, "Probe", 1);
            OwnCarInWorkshop(probe, v.ModelId);
            var order = DisassembleAll(probe);
            var uid = v.Slots.Where(kv => kv.Value.Part is not null).ToDictionary(kv => kv.Key, kv => kv.Value.Part!.Uid);
            DisassembleAll(g);
            g.State.Job!.Budget += 100_000; // new parts rather than repairs, to keep the test short
            foreach (var id in offer.Reqs.SelectMany(q => q.SlotIds))
            {
                var bought = g.BuyPart(ci.Slot(v.ModelId, id).DefaultPart!);
                Assert.True(bought.Ok, bought.Msg);
                uid[id] = bought.Data!;
            }
            var cash = g.State.Cash;
            Reassemble(g, order.Where(uid.ContainsKey).ToList(), id => uid[id]);
            Assert.Null(g.State.Job);
            Assert.True(g.State.Cash > cash);
            Assert.Contains(offer.Thanks, new[] { "Thanks, pal!", "Thanks, friend!", "Great." });
            return;
        }
        Assert.Fail("no phrased job came up");
    }
}
