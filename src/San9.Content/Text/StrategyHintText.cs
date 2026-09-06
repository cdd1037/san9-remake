namespace San9.Content.Text;

public static class StrategyHintText
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[]
    {
        "information", "functions", "help", "pause", "territory", "map-large", "map-small",
        "advance", "facilities", "officers", "units", "overview-forces", "overview-facilities", "overview-units"
    });
    public static string Key(string role) => $"ui.hint.{role}";
}
