// Serializable game state (one mechanic profile). Everything the player does lives here.

using OpenGG.Core.Content;

namespace OpenGG.Core.Sim;

public sealed class PartInstance
{
    public string Uid { get; set; } = "";
    public string PartId { get; set; } = "";
    /// <summary>0 black ... 3 green, see <see cref="Content.Condition"/>.</summary>
    public int Condition { get; set; } = Content.Condition.Good;
    /// <summary>
    /// Where the part sits inside its colour, as in the original: the condition value is
    /// colour / 3 × (1 + extra), the extra being random in about ±0.1 (black is worth 0). Prices follow it.
    /// </summary>
    public double Extra { get; set; }
    /// <summary>Paint on this part (body panels). Null = the car's base paint.</summary>
    public string? Color { get; set; }

    public PartInstance Clone() => new() { Uid = Uid, PartId = PartId, Condition = Condition, Extra = Extra, Color = Color };
}

public sealed class SlotState
{
    public PartInstance? Part { get; set; }
    /// <summary>True = bolt in place and tight. One entry per fastener of the installed part.</summary>
    public List<bool> Fasteners { get; set; } = [];
    /// <summary>Hinged parts only.</summary>
    public bool Open { get; set; }
}

public sealed class DecalState
{
    public string DecalId { get; set; } = "";
    /// <summary>Car-local position and surface normal.</summary>
    public Vec3 Pos { get; set; }
    public Vec3 Normal { get; set; }
    public double Angle { get; set; }
    public double Size { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    /// <summary>Tint picked in the Decal Browser ("#rrggbb"), for decals that take one. Null = as drawn.</summary>
    public string? Color { get; set; }
}

public enum Owner { Player, Customer, Auction }

/// <summary>What the Car Lot shows for each car.</summary>
public sealed class VehicleStats
{
    public decimal OrigCost { get; set; }
    public decimal RepairCost { get; set; }
    /// <summary>Seconds spent in the workshop.</summary>
    public double RepairTime { get; set; }
}

public sealed class VehicleState
{
    public string Id { get; set; } = "";
    public string ModelId { get; set; } = "";
    public Owner Owner { get; set; }
    public string Paint { get; set; } = "#808080";
    /// <summary>The paint picture (see <see cref="PaintCanvas"/>), for cars that have one and have been painted.</summary>
    public string? PaintImage { get; set; }
    public List<DecalState> Decals { get; set; } = [];
    public Dictionary<string, SlotState> Slots { get; set; } = [];
    public VehicleStats Stats { get; set; } = new();
    /// <summary>The Car Lot's Number for a car of yours: how many cars you had bought before it (measured: the
    /// first car bought is 0, then 1, 2 ..., never reused). Null for other people's cars.</summary>
    public int? Number { get; set; }
    /// <summary>The Auction's step for this car, and the car's value when it was set (measured: a car keeps its step
    /// when it comes back to the block, bought at 95 and sold at 95, unless it was changed meanwhile: 70, then 75).</summary>
    public decimal? AuctionStep { get; set; }
    public decimal AuctionStepValue { get; set; }
    /// <summary>"Car Complete" has come up for this car of yours (the original's field 1501 of a car: 0, then 1); its
    /// clock stopped there.</summary>
    public bool Completed { get; set; }
}

public sealed class BinItem
{
    public PartInstance Part { get; set; } = new();
    /// <summary>"catalog", "junkyard" or the id of the vehicle it came off.</summary>
    public string From { get; set; } = "";
}

/// <summary>One condition of a job: these slots must hold a part (one of <see cref="Parts"/>, if set) in at least <see cref="MinCondition"/>.</summary>
public sealed class JobReq
{
    public JobReqType Type { get; set; }
    public List<string> SlotIds { get; set; } = [];
    /// <summary>Acceptable part ids (null = any part that fits).</summary>
    public List<string>? Parts { get; set; }
    public int MinCondition { get; set; } = Condition.Good;
}

public sealed class Job
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Portrait { get; set; } = "";
    public string Text { get; set; } = "";
    public string Nag { get; set; } = "";
    public string Thanks { get; set; } = "";
    public Difficulty Difficulty { get; set; }
    public decimal Fee { get; set; }
    /// <summary>What is left of the fee. Parts and repairs for the job are paid from it; the rest is profit.</summary>
    public decimal Budget { get; set; }
    public string VehicleId { get; set; } = "";
    public List<JobReq> Reqs { get; set; } = [];
    /// <summary>Workshop tabs the job allows (null = all).</summary>
    public List<View>? Tabs { get; set; }
    /// <summary>Job Help texts, shown in turn.</summary>
    public List<string>? Hints { get; set; }
    public int HintIndex { get; set; }
    public string? ThanksPortrait { get; set; }
    /// <summary>The car as it came in (JSON), for Restart.</summary>
    public string? StartVehicle { get; set; }
    /// <summary>Parts Bin contents when the job started (uids), for Restart.</summary>
    public List<string>? StartBin { get; set; }
    /// <summary>JunkYard shelves first stocked while this job was on (car model ids): not kept if it is given up.</summary>
    public List<string>? NewShelves { get; set; }
}

public sealed class JunkItem
{
    public string Id { get; set; } = "";
    public PartInstance Part { get; set; } = new();
    /// <summary>What was charged for it, while it sits in the Purchase Bin (a click there gives it back).</summary>
    public decimal Paid { get; set; }
}

public enum AuctionMode { Buy, Sell }

public enum Bidder { Player, Rival }

public sealed class Bid
{
    public Bidder Who { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Seconds since the car went on the block.</summary>
    public double At { get; set; }
}

public sealed class AuctionResult
{
    public Bidder? Buyer { get; set; }
    public decimal Price { get; set; }
    public decimal? Profit { get; set; }
    /// <summary>Your own car bought back: the sale is off and this service fee is all you pay.</summary>
    public decimal? Fee { get; set; }
}

/// <summary>
/// One car on the block. Measured in the original: it stays up for a fixed time; every bid is at the
/// asking price, which then goes to the bid plus a fixed step; the asking price drops $25 every
/// two seconds; the last bid when time runs out wins.
/// </summary>
public sealed class AuctionState
{
    public AuctionMode Mode { get; set; }
    public string VehicleId { get; set; } = "";
    public decimal CurrentBid { get; set; }
    public decimal Asking { get; set; }
    /// <summary>What each bid adds to the asking price.</summary>
    public decimal Step { get; set; }
    public Bidder? Leader { get; set; }
    /// <summary>Hidden: the most the other bidders will pay.</summary>
    public decimal RivalMax { get; set; }
    public double Elapsed { get; set; }
    public double Duration { get; set; }
    /// <summary>Seconds until this car is on the block: the car before it drives off the stage and this one drives on
    /// (measured: the new figures ~0.75 s after the time ran out or Skip Car); its clock starts then.</summary>
    public double Arriving { get; set; }
    public double NextDropAt { get; set; }
    public double NextRivalIn { get; set; }
    /// <summary>The asking price is past what the other bidders pay: they wait for a drop.</summary>
    public bool RivalsWaiting { get; set; }
    public bool Closed { get; set; }
    public List<Bid> History { get; set; } = [];
    public AuctionResult? Result { get; set; }

    public bool Open => !Closed;
    public double TimeLeft => Math.Max(0, Duration - Elapsed);
}

public enum LogKind { Info, Good, Bad }

public sealed class LogEntry
{
    public string Text { get; set; } = "";
    public LogKind Kind { get; set; }
}

public sealed class GameStats
{
    public int JobsDone { get; set; }
    public int JobsQuit { get; set; }
    public decimal Earned { get; set; }
    public decimal Spent { get; set; }
    /// <summary>Cars bought at the Auction so far; the next one bought gets this as its Number.</summary>
    public int CarsBought { get; set; }
    public int CarsSold { get; set; }
    /// <summary>The CARS column: jobs done plus own cars sold. Feeds the skill level.</summary>
    public int CarsRepaired { get; set; }
}

public sealed class GameState
{
    public int Version { get; set; } = Game.SaveVersion;
    public string ContentId { get; set; } = "";
    public uint Seed { get; set; }
    public uint Rng { get; set; }
    public string Mechanic { get; set; } = "";
    public decimal Cash { get; set; }
    /// <summary>Highest cash ever held; drives the skill level.</summary>
    public decimal BestCash { get; set; }
    public int Skill { get; set; }
    /// <summary>Seconds played.</summary>
    public double PlayTime { get; set; }
    /// <summary>Vehicle in the workshop (job car or your own).</summary>
    public string? Workshop { get; set; }
    /// <summary>Your cars parked on the lot, one per bay from the first, in the order of their Numbers.</summary>
    public List<string> Lot { get; set; } = [];
    public Dictionary<string, VehicleState> Vehicles { get; set; } = [];
    public List<BinItem> Bin { get; set; } = [];
    /// <summary>A job offer waiting for OK / CANCEL.</summary>
    public Job? Offer { get; set; }
    public Job? Job { get; set; }
    /// <summary>JunkYard shelves, one per car model (as in the original's saves), stocked on first use.</summary>
    public Dictionary<string, List<JunkItem>> Shelves { get; set; } = [];
    /// <summary>JunkYard Purchase Bin: bought on the spot, a click gives an item back, leaving takes
    /// everything to the Parts Bin.</summary>
    public List<JunkItem> PurchaseBin { get; set; } = [];
    /// <summary>Decal uses left, by decal id (bought in the Catalog).</summary>
    public Dictionary<string, int> DecalUses { get; set; } = [];
    /// <summary>Car model whose shelf the JunkYard shows.</summary>
    public string? JunkFor { get; set; }
    public AuctionState? Auction { get; set; }
    public List<LogEntry> Log { get; set; } = [];
    public int NextId { get; set; } = 1;
    public GameStats Stats { get; set; } = new();
    /// <summary>Jobs Mode (the original's tutorial chain) until its last job is done, then Free Play.</summary>
    public bool FreePlay { get; set; }
    /// <summary>Scripted jobs finished (template ids).</summary>
    public List<string> CompletedJobs { get; set; } = [];
}
