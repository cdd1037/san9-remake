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
        "officer-detail", "officer-detail-treasures-empty", "officer-detail-treasures", "officer-detail-status", "officer-detail-tactics", "detail-close", "treasure-detail",
        "functions-save", "functions-load", "functions-edit-officers", "functions-edit-corps", "functions-edit-cities",
        "functions-edit-facilities", "functions-edit-obstacles", "functions-pk", "functions-environment",
        "functions-initial", "functions-exit",
        "editor-cancel-officers", "editor-cancel-officers-unchanged", "editor-cancel-corps", "editor-cancel-corps-unchanged",
        "editor-cancel-cities", "editor-cancel-cities-unchanged", "editor-cancel-facilities", "editor-cancel-facilities-unchanged",
        "editor-cancel-obstacles", "editor-cancel-obstacles-unchanged"
    });
    public static string Key(string role) => $"ui.hint.{role}";
}
