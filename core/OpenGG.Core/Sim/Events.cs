namespace OpenGG.Core.Sim;

/// <summary>Result of a game command. On failure <see cref="Msg"/> is a player-facing sentence.</summary>
public readonly record struct Result<T>(bool Ok, T? Data = default, string? Msg = null, string Code = "", IReadOnlyList<string>? Slots = null);

public static class Result
{
    public static Result<T> Success<T>(T data, string? msg = null) => new(true, data, msg);
    public static Result<Unit> Done(string? msg = null) => new(true, Unit.Value, msg);
    public static Result<T> Fail<T>(string msg, string code = "error", IReadOnlyList<string>? slots = null) => new(false, default, msg, code, slots);
    public static Result<Unit> Fail(string msg, string code = "error", IReadOnlyList<string>? slots = null) => new(false, Unit.Value, msg, code, slots);
    public static Result<T> From<T>(Check c) => new(false, default, c.Msg, c.Code, c.Slots);
}

/// <summary>"No data" for <see cref="Result{T}"/>.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

public enum Account { Cash, Job }

public enum AuctionWhat { RivalBid, PlayerBid, Drop, Closed, NextCar }

/// <summary>Things that happened, for the renderer, sounds and UI. State is the source of truth.</summary>
public abstract record GameEvent;
public sealed record ChangedEvent : GameEvent;
public sealed record FastenerEvent(string VehicleId, string SlotId, int Index, bool Removed, string ToolId) : GameEvent;
/// <summary>A part came off or went on; <paramref name="Undo"/>: a part being bolted on went back to the Parts Bin.</summary>
public sealed record PartEvent(string VehicleId, string SlotId, bool Removed, bool Undo = false) : GameEvent;
public sealed record OpenEvent(string VehicleId, string SlotId, bool Open) : GameEvent;
/// <summary>Paint went on a car's paint picture (freehand spraying: many small changes, no full refresh).</summary>
public sealed record PaintEvent(string VehicleId) : GameEvent;
public sealed record MoneyEvent(decimal Delta, Account Account) : GameEvent;
public sealed record ToastEvent(string Message, LogKind Kind) : GameEvent;
public sealed record WorkshopEvent(string? VehicleId) : GameEvent;
/// <summary>A job is finished, raised while its car is still in the WorkShop (it leaves right after).
/// <paramref name="Paid"/> is what goes into your cash: the fee plus what was left of the budget; <paramref name="ModelId"/>
/// the car's model.</summary>
public sealed record JobDoneEvent(Job Job, decimal Paid, string ModelId) : GameEvent;
/// <param name="AfterJob">Earned by a job's pay (the WorkShop behind the box is blank then), not by other money.</param>
public sealed record SkillEvent(int Level, string Name, string From, bool AfterJob = false) : GameEvent;
/// <summary>A car of yours in top condition for the first time: how long it took and what it cost.</summary>
public sealed record CarCompleteEvent(string VehicleId, double Seconds, decimal Spent) : GameEvent;

/// <summary>How "Car Complete" words the time a car took.</summary>
public static class CarCompleteWords
{
    /// <summary>Measured: the hours and the minutes when there are any, the seconds always, "1 hour" but "2 hours" (and "0
    /// second"), joined by "and": "1 hour and 2 minutes and 3 seconds", "2 hours and 1 second", "1 minute and 0 second",
    /// "41 seconds" for 41.2.</summary>
    public static string Took(double seconds)
    {
        long all = (long)Math.Floor(Math.Max(0, seconds));
        long h = all / 3600, m = all % 3600 / 60, s = all % 60;
        static string Unit(long n, string one) => $"{n} {one}{(n > 1 ? "s" : "")}";
        var parts = new List<string>();
        if (h > 0) parts.Add(Unit(h, "hour"));
        if (m > 0) parts.Add(Unit(m, "minute"));
        parts.Add(Unit(s, "second"));
        return string.Join(" and ", parts);
    }
}
public sealed record AuctionEvent(AuctionWhat What, AuctionState Auction) : GameEvent;
