namespace San9.Rendering;

public static class CalendarSeason
{
    public static string ForMonth(int month) => month is < 1 or > 12
        ? throw new ArgumentOutOfRangeException(nameof(month)) : ((month - 1) / 3) switch
        {
            0 => "spring", 1 => "summer", 2 => "autumn", _ => "winter"
        };
}
