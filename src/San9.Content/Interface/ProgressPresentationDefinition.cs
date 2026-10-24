namespace San9.Content.Interface;

/// <summary>Authored reports and strategic context dialogue; source identities remain in the importer.</summary>
public sealed record ProgressPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Records,
    Dictionary<string, DecisionMessageDefinition> Context)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, DecisionMessageDefinition> Introduction { get; init; } = [];
    public static readonly string[] IntroductionRoles = ["opening", "alliance-member", "alliance-target", "enemies", "friends", "economy", "shortage", "ambition"];
    [System.Text.Json.Serialization.JsonRequired]
    public DecisionMessageDefinition OrderPrompt { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired]
    public string StrategyTitleSound { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, DecisionMessageDefinition> MapSpeeches { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, DecisionMessageDefinition> Feedback { get; init; } = [];
    public static readonly string[] FeedbackRoles = ["ability-grown", "tactic-learned", "injury-worsened", "place-commander-changed",
        "unit-commander-changed", "corps-leader-changed", "corps-reorganized", "appearance-introduction", "appearance-greeting", "appearance-result",
        "treasury-captured", "prisoner-escape-speech", "prisoner-escape-result", "battle-capture-speech", "battle-capture-dialogue"];
    public static readonly string[] MapSpeechRoles = ["policy-construction-aborted", "policy-transport-aborted", "policy-return-target-changed",
        "policy-enter-captured-place", "policy-return-after-action", "policy-attack-unit-after-action", "policy-attack-place-after-action", "unit-defeat", "waypoint-reached", "contact-attack", "contact-defense",
        "contact-attack-strong", "contact-attack-weak", "contact-defense-strong", "contact-defense-weak", "contact-building",
        "policy-attack-ordered", "policy-attack-despite-orders", "policy-avoid-ordered", "policy-avoid-despite-orders", "policy-retreat-ordered", "policy-retreat-outnumbered",
        "policy-retreat-low-morale", "policy-retreat-despite-orders", "policy-pursue-ordered", "policy-pursue-despite-orders", "policy-resume-orders", "policy-return-home"];
    public static readonly string[] RecordRoles = ["burn-victim-success", "burn-victim-failure", "burn-food", "alienate-victim-failure", "loyalty-lost", "loyalty-hidden",
        "rumor-observer-success", "rumor-observer-failure", "seize-money-victim", "seize-money-other", "seize-item-victim", "seize-item-other", "seize-person-victim", "seize-person-other",
        "false-report-victim-blocked", "disorder-victim-success", "disorder-victim-blocked", "disorder-other-success", "inspire-other-success", "rescue-other-result",
        "morale-gained", "reinforcement-arrived", "tactic-learned", "tactic-learned-other", "injury-worsened", "place-commander-changed",
        "unit-commander-changed", "corps-leader-changed", "corps-reorganized", "high-spirits-expired", "morale-lost", "unit-morale-collapsed",
        "treasury-captured", "treasury-lost", "battle-captured", "battle-persons-lost", "battle-persons-retreated",
        "policy-construction-aborted", "policy-transport-aborted", "policy-return-target-changed", "policy-enter-captured-place", "policy-return-after-action",
        "policy-attack-unit-after-action", "policy-attack-place-after-action", "unit-lost", "unit-defeated", "unit-lost-other", "territory-changed-other", "facility-destroyed-other", "settlement-won", "settlement-lost",
        "unit-contact", "settlement-contact", "waypoint-reached",
        "unit-recovered", "settlement-recovered", "ability-grown", "secret-contact", "burn-other", "burn-durability",
        "recruit-meeting", "recruit-secret-meeting", "recruit-success", "recruit-failure", "recruit-lost", "recruit-other", "recruit-defected-other",
        "recruit-captive", "recruit-captive-other", "recruit-unit", "recruit-unit-lost", "recruit-place", "recruit-place-lost",
        "policy-attack-ordered", "policy-attack-despite-orders", "policy-avoid-ordered", "policy-avoid-despite-orders", "policy-retreat-ordered", "policy-retreat-outnumbered",
        "policy-retreat-low-morale", "policy-retreat-despite-orders", "policy-pursue-ordered", "policy-pursue-despite-orders", "policy-resume-orders", "policy-return-home",
        "territory-changed", "territory-lost", "facility-won", "facility-destroyed", "construction-completed", "transport-completed", "person-captured",
        "enemy-captured", "ruler-succeeded", "force-eliminated", "natural-death", "ruler-death", "captive-death", "melee-death", "sniping-death", "duel-death"];
    public static readonly string[] ContextRoles = ["enemy-city", "enemy-place", "food-shortage", "money-shortage", "reinforce-from", "reinforce-to",
        "defense-desperate", "defense-rockfall", "defense-siege", "defense-city", "defense-place", "defense-counterattack",
        "person-city", "person-place", "person-city-anonymous", "person-place-anonymous", "person-region",
        "treasure-city", "treasure-place", "treasure-city-anonymous", "treasure-place-anonymous", "treasure-region",
        "ruler-health", "adviser-health", "officer-health"];
}
