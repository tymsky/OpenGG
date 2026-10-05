// Content pack model.
//
// The engine never hard-codes cars, parts, jobs or prices: everything comes from a ContentPack.
// The bundled pack ("ai") is generated placeholder content (files prefixed with ai_, see
// tools/gen/types.ts for the JSON schema). The "original" pack is built at runtime from the
// player's own copy of the original game (OpenGG.Core.Original).

namespace OpenGG.Core.Content;

/// <summary>The three work areas of a car, as in the original game.</summary>
public enum Region { Engine, Body, RunningGear }

/// <summary>The workshop tabs: the three regions plus the complete car.</summary>
public enum View { Complete, Engine, Body, RunningGear }

public static class Regions
{
    public static readonly Region[] All = [Region.Engine, Region.Body, Region.RunningGear];

    public static View ToView(this Region r) => r switch
    {
        Region.Engine => View.Engine,
        Region.Body => View.Body,
        _ => View.RunningGear,
    };

    public static Region? ToRegion(this View v) => v switch
    {
        View.Engine => Region.Engine,
        View.Body => Region.Body,
        View.RunningGear => Region.RunningGear,
        _ => null,
    };

    public static string Label(this Region r) => r switch
    {
        Region.Engine => "engine",
        Region.Body => "body",
        _ => "running gear",
    };
}

/// <summary>
/// Part condition, the original's four colours: 3 green (good), 2 yellow (minor damage),
/// 1 red (major damage, still repairable), 0 black (destroyed: can only be scrapped).
/// </summary>
public static class Condition
{
    public const int Black = 0;
    public const int Red = 1;
    public const int Yellow = 2;
    public const int Green = 3;
    public const int Good = Green;

    public static readonly int[] All = [Black, Red, Yellow, Green];
    public static readonly string[] Names = ["Destroyed", "Major damage", "Minor damage", "Good"];
    public static readonly string[] Colors = ["#1a1a1a", "#d8352a", "#e8c030", "#46c25a"];
}

public enum FastenerVisual { Cube, Hex, Lug, None }

/// <summary>When a part's own sound plays: the starter's when the engine is started, the engine and
/// exhaust parts' while it runs, an accessory's (a horn) on its own.</summary>
public enum PartSound { Run, Start, Accessory }

public sealed class FastenerKindDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>How the engine draws it. The original shows bolts as small white cubes.</summary>
    public FastenerVisual Visual { get; set; } = FastenerVisual.Cube;
    /// <summary>Half-size in meters (rendering and picking).</summary>
    public double Size { get; set; } = 0.01;
}

/// <summary>
/// Pointer: the hand (take parts off, put them on). Fasten: undoes and tightens bolts of the
/// listed kinds. Paint: body paint and decals. Camera: snapshot. StartEngine: turn the key.
/// </summary>
public enum ToolAction { Pointer, Fasten, Paint, Camera, StartEngine }

public sealed class ToolDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ToolAction Action { get; set; }
    public List<string> FastenerKinds { get; set; } = [];
    /// <summary>Tabs where the tool is offered.</summary>
    public List<View> Regions { get; set; } = [];
    public string Icon { get; set; } = "";
    public string? Sound { get; set; }
    public string? Hotkey { get; set; }
}

public sealed class FastenerDef
{
    public string Kind { get; set; } = "";
    /// <summary>Part-local position.</summary>
    public Vec3 Pos { get; set; }
    /// <summary>Part-local unit vector pointing out of the surface (unscrew direction). Zero = let the renderer work it out.</summary>
    public Vec3 Dir { get; set; } = Vec3.Up;
}

public sealed class PartDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Catalog grouping (engine, electrical, glass, wheels...).</summary>
    public string Category { get; set; } = "";
    /// <summary>Slots with the same slot type accept this part.</summary>
    public string SlotType { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Catalog (new) price.</summary>
    public decimal Price { get; set; }
    /// <summary>Lowest price of the part (the original stores a cost range). Null = no range.</summary>
    public decimal? PriceMin { get; set; }
    public List<FastenerDef> Fasteners { get; set; } = [];
    /// <summary>Aftermarket / custom part.</summary>
    public bool Custom { get; set; }
    /// <summary>Added to the car value while installed.</summary>
    public decimal ValueBonus { get; set; }
    public string? Description { get; set; }
    /// <summary>Slots that must be filled before this particular part can go on (the original's per-part AttachDep).
    /// Adds to the slot's <see cref="SlotDef.Parents"/>.</summary>
    public List<string>? Mounts { get; set; }
    /// <summary>One filled slot of <see cref="Mounts"/> is enough (the original's AMEA parts).</summary>
    public bool MountsAny { get; set; }
    /// <summary>The very parts this one goes on, where an alternative could take their place: with the alternative on, it
    /// does not go on (the original's AttachDep without AMEA: an Escort's Back Windshield goes on the Trunk, not on the
    /// Hatchback Cosworth in its place). Null = any part in the slots it goes on will do.</summary>
    public List<string>? NeedsParts { get; set; }
    /// <summary>Slots that must be empty before this part can come off (the original's RemoveDep).</summary>
    public List<string>? RemoveAfter { get; set; }
    /// <summary>The part's own sound (asset id), e.g. a starter or a big engine in the original's cars.</summary>
    public string? Sound { get; set; }
    public PartSound SoundKind { get; set; }
    /// <summary>Turns while the engine runs: a crankshaft, a flywheel, a fan (the original's "spinner" parts).</summary>
    public bool Spins { get; set; }
    /// <summary>The engine itself: its block or case, which shakes while it runs and carries its running sound (the
    /// original's special kind 1). Its wear does not make the starter click (see <see cref="DiagnosisDef.EngineClicksBelow"/>);
    /// worn out, the engine cranks without catching (<see cref="DiagnosisDef.BlockCranksBelow"/>).</summary>
    public bool Block { get; set; }
    /// <summary>Not part of the stock car though the file does not mark it custom: it goes on a custom part (a T-Bird's
    /// Blower on its HotRod manifold). Such parts come with the custom ones (see <see cref="Sim.Game"/>'s JunkYard
    /// shelves).</summary>
    public bool AddOn { get; set; }
}

public sealed class OpenableDef
{
    public Vec3 Axis { get; set; } = new(0, 0, 1);
    public double Angle { get; set; }
}

public sealed class SlotDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SlotType { get; set; } = "";
    /// <summary>Functional family shared across cars (e.g. "air_filter"); used by jobs and diagnosis.</summary>
    public string Family { get; set; } = "";
    public Region Region { get; set; }
    /// <summary>JSON form of a single parent. Merged into <see cref="Parents"/> by <see cref="ContentPack.Normalize"/>.</summary>
    public string? Parent { get; set; }
    /// <summary>Slots this part is mounted on. They must be in place to install it, and it must come off before them.</summary>
    public List<string> Parents { get; set; } = [];
    /// <summary>Slots whose parts must be removed (or opened) before this one is reachable.</summary>
    public List<string> BlockedBy { get; set; } = [];
    /// <summary>Car-local position of the part origin.</summary>
    public Vec3 Pos { get; set; }
    /// <summary>Car-local Euler rotation (XYZ order, radians).</summary>
    public Vec3 Rot { get; set; }
    /// <summary>Car-local direction the part travels when taken off.</summary>
    public Vec3 RemoveDir { get; set; } = Vec3.Up;
    public bool Required { get; set; }
    public string? DefaultPart { get; set; }
    /// <summary>Hinged parts (hood). Rotation around a part-local axis through the part origin.</summary>
    public OpenableDef? Openable { get; set; }
}

public sealed class CarDims
{
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Wheelbase { get; set; }
}

/// <summary>A piece of the car that never comes off (frame, shell), shown with its region.</summary>
public sealed class StaticModelDef
{
    public string Model { get; set; } = "";
    public Region Region { get; set; } = Region.Body;
}

public sealed class CarModelDef
{
    public string Id { get; set; } = "";
    public string Make { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }
    public string BodyStyle { get; set; } = "";
    public string EngineLabel { get; set; } = "";
    /// <summary>The static shell (floor, pillars, frame...). Panels are parts.</summary>
    public string BodyModel { get; set; } = "";
    /// <summary>Material name in the body and panel models that receives the paint colour.</summary>
    public string PaintMaterial { get; set; } = "paint";
    /// <summary>The paint faces map into a 256 x 256 paint picture, one square per panel (the original's
    /// cars): paint can then be sprayed on freehand. Otherwise each panel has one colour.</summary>
    public bool PaintAtlas { get; set; }
    public string DefaultPaint { get; set; } = "#808080";
    /// <summary>The colour the model itself is painted in (the original's cars: their paint materials), or "".</summary>
    public string OwnPaint { get; set; } = "";
    /// <summary>Value of a perfect, stock car.</summary>
    public decimal BaseValue { get; set; }
    /// <summary>Skill level needed before it shows up.</summary>
    public int MinSkill { get; set; }
    public CarDims Dims { get; set; } = new();
    public List<SlotDef> Slots { get; set; } = [];
    /// <summary>Extra fixed pieces per region (the original's shell and frame meshes).</summary>
    public List<StaticModelDef> Statics { get; set; } = [];

    /// <summary>"1977 Ford Pickup" style display name.</summary>
    public string DisplayName => string.Join(' ', new[] { Year > 0 ? Year.ToString() : "", Make, Name }.Where(s => s.Length > 0));
}

public enum Difficulty { Easy, Medium, Hard, Expert }

/// <summary>
/// Fix: parts in these families arrive damaged and must end up green.
/// Missing: these slots arrive empty and must be filled with a green part.
/// Install: one of <see cref="JobRequirementDef.Parts"/> must go into a slot of these families.
/// </summary>
public enum JobReqType { Fix, Missing, Install, Remove }

public sealed class JobRequirementDef
{
    public JobReqType Type { get; set; }
    public List<string> Families { get; set; } = [];
    public int[] Count { get; set; } = [1, 1];
    public List<string>? Parts { get; set; }
}

public sealed class JobTemplateDef
{
    public string Id { get; set; } = "";
    /// <summary>Request text. Words between *asterisks* are highlighted. {name} = customer, {car} = car name.</summary>
    public string Text { get; set; } = "";
    /// <summary>Shown by Job Help while you work.</summary>
    public string? Nag { get; set; }
    /// <summary>Shown when the job is done.</summary>
    public string? Thanks { get; set; }
    public Difficulty Difficulty { get; set; }
    public List<JobRequirementDef> Requirements { get; set; } = [];
    public double FeeMultiplier { get; set; } = 1;
    public double Weight { get; set; } = 1;
    /// <summary>Only for these body styles (all if null).</summary>
    public List<string>? BodyStyles { get; set; }
    /// <summary>Workshop tabs the job allows (all if null). The original hides the others.</summary>
    public List<View>? Tabs { get; set; }
    /// <summary>A Free Play job whose damage and words are made up on the spot from the pack's
    /// <see cref="ContentPack.Phrases"/> (as the original's random jobs), rather than from Requirements.</summary>
    public bool Phrased { get; set; }

    // ---- scripted jobs (the original's job packs) ----
    /// <summary>Position in the Jobs Mode chain (null = random job for Free Play).</summary>
    public int? Sequence { get; set; }
    /// <summary>The car this job is for.</summary>
    public string? CarId { get; set; }
    /// <summary>Fixed fee (null = worked out from the parts and labour).</summary>
    public decimal? Fee { get; set; }
    public string? Portrait { get; set; }
    public string? ThanksPortrait { get; set; }
    /// <summary>Job Help texts, shown in turn.</summary>
    public List<string>? Hints { get; set; }
    /// <summary>The car's paint colour ("#rrggbb").</summary>
    public string? Paint { get; set; }
    /// <summary>How the car comes in. Applied in order over a car in perfect condition.</summary>
    public List<JobStatDef>? Start { get; set; }
    /// <summary>What finishes the job.</summary>
    public List<JobStatDef>? Complete { get; set; }
}

/// <summary>
/// One line of a scripted job's start or finish state: a region (or the whole car) or one part,
/// and a condition (-1 = absent, 0 black ... 3 green).
/// </summary>
public sealed class JobStatDef
{
    /// <summary>"all", "engine", "body" or "running_gear" for a region line; null for a part line.</summary>
    public string? Region { get; set; }
    /// <summary>Part id for a part line.</summary>
    public string? Part { get; set; }
    public int Condition { get; set; } = Content.Condition.Green;

    public const int Absent = -1;
}

/// <summary>
/// Words for random job requests, as the original keeps them for its random jobs: variables, sentences by
/// what is wrong with the car, and customers with their faces and lines. Texts use %Variable; %PartName is
/// the name of the part a sentence is about.
/// </summary>
public sealed class JobPhraseBook
{
    public Dictionary<string, PhraseVariable> Variables { get; set; } = [];
    public List<PhraseSentence> Sentences { get; set; } = [];
    public List<PhraseCustomer> Customers { get; set; } = [];
}

/// <summary>How a variable picks its word.</summary>
public enum PhraseChoice
{
    /// <summary>Any of them.</summary>
    Random,
    /// <summary>By how bad the damage is told: the first word the mildest, the last the strongest (measured:
    /// only those two ever come up).</summary>
    Condition,
    /// <summary>By how much of the region is damaged, in even bands: the first word for the least, the last for
    /// nearly all of it.</summary>
    Share,
    /// <summary>By the region: the whole car, engine, body, running gear (in the order of <see cref="View"/>).</summary>
    Region,
}

public sealed class PhraseVariable
{
    public PhraseChoice Choice { get; set; }
    public List<string> Words { get; set; } = [];
}

/// <summary>What a sentence tells: a symptom or a cause of damage in a region, how much of a region is
/// damaged, or one part that is damaged or missing.</summary>
public enum PhraseKind { Symptom, Cause, RegionShare, PartDamaged, PartMissing }

public sealed class PhraseSentence
{
    public PhraseKind Kind { get; set; }
    /// <summary>Symptoms and causes: the region they are about (Complete = the whole car).</summary>
    public View? Region { get; set; }
    public string Text { get; set; } = "";
}

public sealed class PhraseCustomer
{
    /// <summary>Portrait asset id while the customer waits for the car.</summary>
    public string Face { get; set; } = "";
    /// <summary>Portrait asset id when the job is done.</summary>
    public string HappyFace { get; set; } = "";
    /// <summary>Said before the damage (it may end in "but " and run on into it).</summary>
    public string Before { get; set; } = "";
    /// <summary>Said after the damage.</summary>
    public string After { get; set; } = "";
    public string Thanks { get; set; } = "";
}

public sealed class DecalDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Texture { get; set; } = "";
    public decimal Price { get; set; }
    /// <summary>Width / height.</summary>
    public double Aspect { get; set; } = 1;
    /// <summary>How many times one purchase can be applied.</summary>
    public int Uses { get; set; } = 1;
    /// <summary>The Decal Browser's palette recolours it (a white or single-colour picture).</summary>
    public bool Tint { get; set; }
}

public sealed class PaintDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"#rrggbb".</summary>
    public string Color { get; set; } = "#808080";
}

public sealed class SkillLevelDef
{
    public string Name { get; set; } = "";
    /// <summary>Needs your cash to have been at least this much at some point...</summary>
    public decimal Cash { get; set; }
    /// <summary>...and at least this many cars repaired (jobs done plus own cars sold).</summary>
    public int Cars { get; set; }
    /// <summary>Job difficulties offered at this level.</summary>
    public List<Difficulty> Difficulties { get; set; } = [];
}

/// <summary>One kind of auction car: the share of its stock parts that is gone, the odds of the colours of the rest
/// ([black, red, yellow, green]), and whether it is of the badly beaten-up kind (see
/// <see cref="RulesDef.AuctionBeatenShare"/>), and of those, a wreck as the top skill's are.</summary>
public sealed record AuctionCarDef(double Missing, double[] Colours, bool Beaten = false, bool Wreck = false);

/// <summary>Which part families make the engine misbehave when missing or broken (Start Engine).</summary>
public sealed class DiagnosisDef
{
    /// <summary>Nothing at all (no power).</summary>
    public List<string> Dead { get; set; } = [];
    /// <summary>Clicks, won't crank.</summary>
    public List<string> NoCrank { get; set; } = [];
    /// <summary>Cranks but won't start.</summary>
    public List<string> NoStart { get; set; } = [];
    /// <summary>Runs, but knocks or shakes.</summary>
    public List<string> Rough { get; set; } = [];
    /// <summary>Runs, but very loud.</summary>
    public List<string> Loud { get; set; } = [];
    /// <summary>Minimum condition that counts as working.</summary>
    public int WorkingCondition { get; set; } = Condition.Red;
    /// <summary>Any part of the engine on the car in worse shape than this, its block aside, makes the starter click, as
    /// with no starter, before anything missing is looked at (the original's rule: red or black clicks, a yellow part
    /// still starts); null: only the families count.</summary>
    public int? EngineClicksBelow { get; set; }
    /// <summary>The engine's block in worse shape than this: it cranks without catching, as with a part missing (the
    /// original's rule: a whole engine with only its block red cranked); null: the block's wear stops nothing.</summary>
    public int? BlockCranksBelow { get; set; }
}

/// <summary>
/// Tunable rules. Money formulas that were measured in the original are fixed in
/// <see cref="Sim.Economy"/>; what is here is either measured too (noted) or still a guess.
/// </summary>
public sealed class RulesDef
{
    /// <summary>Measured: a new mechanic starts with $5000 in the 2002 release.</summary>
    public decimal StartCash { get; set; }
    public List<SkillLevelDef> Skills { get; set; } = [];
    /// <summary>A part's condition is its colour plus a random extra in ±this (measured: ±0.1).</summary>
    public double ConditionSpread { get; set; } = 0.1;
    /// <summary>Condition odds for junkyard stock, by condition (measured: 986 parts of 26 new shelves, 23 % black, 25 %
    /// red, 25 % yellow, 27 % green). Black stock is red by the time the yard shows it, so OpenGG stocks a new shelf's as
    /// red.</summary>
    public double[] JunkConditionWeights { get; set; } = [1, 1, 1, 1];
    /// <summary>How many parts of a new JunkYard shelf are picked at random, [min, max], after those it gets for the car
    /// that came in (measured: 15 to 21 on 21 new shelves).</summary>
    public int[] JunkShelfRandom { get; set; } = [15, 21];
    /// <summary>Shelf items that come and go by themselves after each job, [min, max]. Measured: none; a shelf changes by
    /// itself only as the yard is gone to (<see cref="JunkVisitGone"/>).</summary>
    public int[] JunkChurn { get; set; } = [0, 0];
    /// <summary>How the shelf of the car shown changes by itself each time the JunkYard is gone to: the odds of 0, 1, 2
    /// or 3 of its parts going, and of 0 to 3 new ones coming. Measured on 28 visits to one shelf, from the save the
    /// original writes at each: 5, 13, 6 and 4 visits lost 0 to 3; 5, 12, 9 and 2 gained 0 to 3.</summary>
    public double[] JunkVisitGone { get; set; } = [5, 13, 6, 4];
    public double[] JunkVisitNew { get; set; } = [5, 12, 9, 2];
    /// <summary>Job fees from which the Job Request says Medium, Hard and Expert (below the first: Easy).
    /// Measured on 417 offers: $245.63 Easy, $250.00 Medium, $548.22 Medium, $553.67 Hard, $1049.90 Hard,
    /// $1050.31 Expert.</summary>
    public decimal[] DifficultyFees { get; set; } = [250, 550, 1050];
    /// <summary>Condition odds for parts that are not part of the job, in customer cars.</summary>
    public double[] JobWearWeights { get; set; } = [0, 0, 0, 1];
    /// <summary>Condition odds for damaged job parts ("fix" requirements).</summary>
    public double[] JobDamageWeights { get; set; } = [0, 1, 1, 0];
    /// <summary>Condition odds for parts of auction cars (without <see cref="AuctionCars"/>).</summary>
    public double[] AuctionWearWeights { get; set; } = [0, 1, 1, 1];
    public double AuctionMissingChance { get; set; }
    /// <summary>
    /// What the original's auction cars were like when bought (18 cars, their saves read): how much of the stock car was
    /// gone and the colours of what was left, [black, red, yellow, green]. Each car on the block takes one of them, what
    /// is gone taken off from the outside in (nothing left hanging, as in every one of them). Three kinds: every car bought
    /// at Novice (five) was fairly whole, 10 to 15 % gone, its parts red, yellow and green alike; every one bought at
    /// Expert (thirteen) was badly beaten up, 41 to 79 % gone, its parts mostly black and red; the seven bought at the top
    /// skill were wrecks, 73 to 96 % gone (a Miata with 2 of its 55 parts).
    /// </summary>
    public List<AuctionCarDef> AuctionCars { get; set; } =
    [
        new(0.10, [4, 18, 16, 14]), new(0.14, [1, 12, 6, 6]), new(0.15, [0, 2, 10, 10]), new(0.13, [0, 12, 11, 10]),
        new(0.15, [0, 14, 21, 18]),
        new(0.41, [27, 17, 0, 0], true), new(0.41, [19, 23, 2, 0], true), new(0.65, [7, 6, 1, 0], true),
        new(0.47, [1, 23, 7, 0], true), new(0.57, [0, 1, 6, 2], true), new(0.54, [0, 5, 5, 2], true),
        new(0.58, [0, 17, 8, 0], true), new(0.48, [12, 20, 0, 0], true), new(0.56, [17, 20, 0, 0], true),
        new(0.42, [22, 16, 0, 0], true), new(0.47, [5, 26, 8, 0], true), new(0.53, [9, 18, 0, 0], true),
        new(0.79, [3, 3, 1, 0], true),
        new(0.96, [0, 2, 0, 0], true, true), new(0.86, [4, 5, 0, 0], true, true), new(0.83, [4, 3, 2, 0], true, true),
        new(0.90, [6, 1, 0, 0], true, true), new(0.73, [7, 9, 3, 0], true, true), new(0.80, [11, 7, 4, 0], true, true),
        new(0.88, [1, 2, 5, 0], true, true),
    ];
    /// <summary>
    /// The share of the badly beaten-up kind of <see cref="AuctionCars"/> at each skill, Learning to Mekada ("auction cars
    /// get more beaten up as your skill rises", players said). Measured: none at Novice (its 16 steps watched, 45 to 490,
    /// come out as the fairly whole cars' value would give them); at Expert about two in three (a script buying cars
    /// with a step up to $110 found one in about 57 % of the cars it saw, and every one it bought was beaten up). Handy
    /// was not seen: halfway, a guess.
    /// </summary>
    public double[] AuctionBeatenShare { get; set; } = [0, 0, 0.35, 0.65, 0.65];
    /// <summary>Fee = (parts * FeePartFactor + labor) * difficulty factor * template factor.</summary>
    public double FeePartFactor { get; set; } = 1;
    public decimal LaborPerPart { get; set; }
    public decimal LaborPerFastener { get; set; }
    /// <summary>Keyed by difficulty name ("easy", "medium"...).</summary>
    public Dictionary<string, double> DifficultyFee { get; set; } = [];
    /// <summary>Measured: every car is on the block for about 20.5 seconds, bids or not.</summary>
    public double AuctionDuration { get; set; } = 20.5;
    /// <summary>Measured: the asking price drops by <see cref="AuctionDrop"/> every this many seconds.</summary>
    public double AuctionTick { get; set; } = 2;
    public decimal AuctionDrop { get; set; } = 25;
    /// <summary>Measured: every car goes on the block at a Current Bid of $100, yours too; the asking price
    /// starts a step above it.</summary>
    public decimal AuctionOpeningBid { get; set; } = 100;
    /// <summary>No longer used: the step follows the car as it is (<see cref="Sim.Economy.AuctionStep"/>). Once a fraction
    /// of the car's value drawn at random (three cars 6.3–7.5 % of their parts' JunkYard value).</summary>
    public double[] AuctionStep { get; set; } = [0.06, 0.075];
    /// <summary>The other bidders go on while the asking price is at most the opening bid plus this many
    /// steps (measured on 11 cars: they stopped 12.7–15.1 steps up). Used where <see cref="AuctionRivalSpread"/> is not
    /// given.</summary>
    public double[] AuctionRivalSteps { get; set; } = [12.7, 15.1];
    /// <summary>
    /// How many steps above the opening bid the other bidders go, at each skill from Learning to Mekada: the spread
    /// measured, as its lowest, lower quartile, median, upper quartile and highest; each time a car goes on the block its
    /// limit is drawn anew from it (the same car went 5.7 steps up once and 10.6 the next time). Measured at Novice (25
    /// cars: 9.8 to 15.1, most of them 13 to 14.5) and at Mekada (43 cars: 3.0 to 14.5, half of them under 8.6); Handy
    /// and Expert are a third and two thirds of the way, a guess. Null: <see cref="AuctionRivalSteps"/> for all.
    /// </summary>
    public double[][]? AuctionRivalSpread { get; set; } =
    [
        [9.8, 13.0, 13.8, 14.4, 15.1], [9.8, 13.0, 13.8, 14.4, 15.1], [7.5, 10.8, 12.1, 12.9, 14.9],
        [5.3, 8.7, 10.3, 11.4, 14.7], [3.0, 6.5, 8.6, 9.95, 14.5],
    ];
    /// <summary>Outbidding everybody for your own car calls the sale off: the service fee is this share of your bid, in
    /// whole $5 (measured: $40 at $825, $205 at $4150); the car stays yours as it was.</summary>
    public decimal AuctionBuyBackFee { get; set; } = 0.05m;
    /// <summary>Measured (the auctioneer's calls, one to each rival bid, timed to the millisecond: 65 bids of five cars):
    /// the other bidders bid on a beat of this many seconds...</summary>
    public double AuctionBeat { get; set; } = 0.755;
    /// <summary>...letting about one beat in six go by without a bid.</summary>
    public double AuctionBeatSkip { get; set; } = 0.17;
    public decimal PaintPrice { get; set; }
    public DiagnosisDef Diagnosis { get; set; } = new();

    public double DifficultyFactor(Difficulty d) =>
        DifficultyFee.TryGetValue(d.ToString().ToLowerInvariant(), out double f) ? f : 1;
}

public sealed class NameLists
{
    public List<string> First { get; set; } = [];
    public List<string> Last { get; set; } = [];
}

public sealed class ContentPack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ToolDef> Tools { get; set; } = [];
    public List<FastenerKindDef> FastenerKinds { get; set; } = [];
    public List<PartDef> Parts { get; set; } = [];
    public List<CarModelDef> Cars { get; set; } = [];
    public List<JobTemplateDef> Jobs { get; set; } = [];
    public NameLists Names { get; set; } = new();
    /// <summary>Portrait asset ids for customers.</summary>
    public List<string> Portraits { get; set; } = [];
    public List<DecalDef> Decals { get; set; } = [];
    public List<PaintDef> Paints { get; set; } = [];
    public RulesDef Rules { get; set; } = new();
    /// <summary>Words for the Phrased random jobs (null: the pack has none).</summary>
    public JobPhraseBook? Phrases { get; set; }

    /// <summary>Folds JSON conveniences into the canonical model (single parent → parent list).</summary>
    public void Normalize()
    {
        foreach (var car in Cars)
            foreach (var s in car.Slots)
            {
                if (s.Parent is { } p && !s.Parents.Contains(p)) s.Parents.Insert(0, p);
                s.Parent = null;
            }
    }
}

/// <summary>Maps logical asset ids (used in content) to files, relative to the manifest.</summary>
public sealed class AssetManifest
{
    public string Pack { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
    public Dictionary<string, string> Models { get; set; } = [];
    public Dictionary<string, string> Textures { get; set; } = [];
    public Dictionary<string, string> Sounds { get; set; } = [];
    public Dictionary<string, string> Icons { get; set; } = [];
}
