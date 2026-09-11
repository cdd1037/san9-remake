using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public enum XunIndex { Early = 1, Middle = 2, Late = 3 }

/// <summary>The authoritative 360-day calendar. Periods are derived, never stored separately.</summary>
public readonly record struct GameClock
{
    public GameClock(int year, int month, XunIndex xun)
        : this(year, month, Enum.IsDefined(xun) ? 1 + 10 * ((int)xun - 1) : 0) { }

    [JsonConstructor]
    public GameClock(int year, int month, int day)
    {
        if (year <= 0) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (day is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(day));
        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    [JsonIgnore] public XunIndex Xun => (XunIndex)((Day - 1) / 10 + 1);
    [JsonIgnore] public long DayNumber => ((long)Year - 1) * 360 + (Month - 1) * 30 + Day - 1;
    [JsonIgnore] public bool IsXunBoundary => Day is 1 or 11 or 21;
    [JsonIgnore] public PeriodBoundary Boundary => new(IsXunBoundary, Day == 1,
        Day == 1 && Month is 1 or 4 or 7 or 10, Day == 1 && Month == 1);

    public GameClock NextDay() => Day < 30 ? new(Year, Month, Day + 1)
        : Month < 12 ? new(Year, Month + 1, 1) : new(checked(Year + 1), 1, 1);

    public GameClock Advance()
    {
        var next = NextDay();
        while (!next.IsXunBoundary) next = next.NextDay();
        return next;
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2}";
}

public readonly record struct PeriodBoundary(bool Xun, bool Month, bool Quarter, bool Year);
