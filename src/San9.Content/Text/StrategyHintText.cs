namespace San9.Content.Text;

public static class StrategyHintText
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[]
    {
        "information", "functions", "help", "pause", "territory", "map-large", "map-small",
        "advance", "facilities", "officers", "units", "overview-forces", "overview-facilities", "overview-units",
        "information-ranks", "information-forces", "information-corps", "information-cities", "information-regions",
        "information-units", "information-officers", "information-treasures", "information-records", "information-chronicle",
        "information-units-empty", "information-treasures-empty", "catalog-officers",
        "officer-detail", "officer-detail-treasures-empty", "officer-detail-treasures", "officer-detail-status", "officer-detail-tactics", "detail-close", "treasure-detail"
    });
    public static string Key(string role) => $"ui.hint.{role}";
}
