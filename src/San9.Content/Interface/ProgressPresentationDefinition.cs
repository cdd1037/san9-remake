namespace San9.Content.Interface;

/// <summary>Authored reports and strategic context dialogue; source identities remain in the importer.</summary>
public sealed record ProgressPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Records,
    Dictionary<string, DecisionMessageDefinition> Context)
{
    [System.Text.Json.Serialization.JsonRequired]
    public DecisionMessageDefinition OrderPrompt { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired]
    public string StrategyTitleSound { get; init; } = null!;
    public static readonly string[] RecordRoles = ["unit-lost", "unit-defeated", "settlement-won", "settlement-lost",
        "territory-changed", "territory-lost", "facility-won", "facility-destroyed", "construction-completed", "transport-completed", "person-captured",
        "enemy-captured", "prisoner-escaped", "ruler-succeeded", "force-eliminated", "natural-death", "ruler-death", "captive-death", "melee-death", "sniping-death", "duel-death"];
    public static readonly string[] ContextRoles = ["enemy-city", "enemy-place", "food-shortage", "money-shortage", "reinforce-from", "reinforce-to",
        "defense-desperate", "defense-rockfall", "defense-siege", "defense-city", "defense-place", "defense-counterattack",
        "person-city", "person-place", "person-city-anonymous", "person-place-anonymous", "person-region",
        "treasure-city", "treasure-place", "treasure-city-anonymous", "treasure-place-anonymous", "treasure-region",
        "ruler-health", "adviser-health", "officer-health"];
}
