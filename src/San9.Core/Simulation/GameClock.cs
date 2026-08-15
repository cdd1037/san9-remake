namespace San9.Core.Simulation;

public enum XunIndex
{
    Early = 1,
    Middle = 2,
    Late = 3,
}

public readonly record struct GameClock
{
    public GameClock(int year, int month, XunIndex xun)
    {
        if (year <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        if (!Enum.IsDefined(xun))
        {
            throw new ArgumentOutOfRangeException(nameof(xun));
        }

        Year = year;
        Month = month;
        Xun = xun;
    }

    public int Year { get; }

    public int Month { get; }

    public XunIndex Xun { get; }

    public GameClock Advance()
    {
        if (Xun != XunIndex.Late)
        {
            return new GameClock(Year, Month, (XunIndex)((int)Xun + 1));
        }

        return Month == 12
            ? new GameClock(Year + 1, 1, XunIndex.Early)
            : new GameClock(Year, Month + 1, XunIndex.Early);
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}-X{(int)Xun}";
}
