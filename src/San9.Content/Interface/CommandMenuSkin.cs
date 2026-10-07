namespace San9.Content.Interface;

/// <summary>Semantic art and text roles; source resource numbers belong to the importer.</summary>
public static class CommandMenuSkin
{
    public static IReadOnlyList<string> EntryIds { get; } = Array.AsReadOnly(new[] {
        "facilities", "military", "talent", "strategy", "diplomacy", "appointments", "patrol", "commerce", "cultivate", "repair", "recruit", "train", "buy", "sell", "demolish", "sortie", "build", "transport", "summon", "move", "search", "employ", "alienate", "burn", "seize", "rumor", "false-report", "disorder", "inspire", "rescue", "gift", "request", "return", "surrender", "delegate", "strategist", "rank", "reward", "bestow", "confiscate", "execute", "exile", "attack", "march", "redirect-transport"
    });
    public const string OfficerSelectionHintKey = "ui.command-select-officers";
    public static string DescriptionKey(string id) => $"ui.command.{id}";
    public static string HintKey(string id) => $"ui.command-hint.{id}";
    public static IReadOnlyList<string> ReasonIds { get; } = Array.AsReadOnly(new[] { "unavailable", "already-used", "public-order-max", "no-officer", "funds", "confused", "engaged",
        "commerce-max", "cultivation-max", "durability-max", "no-soldiers", "morale-max", "soldiers-max", "no-recruits", "merchant-absent", "food-max", "money-max", "no-food", "insufficient-food", "no-funds", "recruit-no-funds", "recruit-used", "trade-used",
        "no-move-destination", "no-other-facility", "no-construction-site", "no-employ-target", "no-alienate-target", "no-target-facility",
        "no-false-report-target", "no-disorder-target", "no-inspire-target", "no-rescue-target", "rescue-soldiers", "no-rumor-forces",
        "no-rumor-officers", "no-target-force", "no-gift-resources", "no-captured-officer", "unit-routed", "unit-confused", "unit-no-formation", "unit-false-report",
        "no-adviser-candidate", "reward-funds", "no-stationed-officer", "loyalty-max", "no-reward-candidate", "no-treasure", "no-treasure-holder",
        "no-stationed-treasure-holder", "no-prisoner", "no-exile-candidate", "no-subordinate", "no-office-candidate", "no-office",
        "no-delegation-city", "no-corps-leader", "no-normal-facility", "no-recall-candidate" });
    public static string ReasonKey(string id) => $"ui.command-reason.{id}";
    public static IEnumerable<string> RequiredRoles => EntryIds.Select(id => $"command-illustration-{id}")
        .Concat(new[] { "top", "middle", "bottom", "illustration-frame", "condition", "summary-city", "summary-facility", "summary-unit", "summary-merchant", "summary-training" }.Select(id => "command-" + id))
        .Concat(new[] { "primary", "secondary" }.SelectMany(level => new[] { "blue", "green", "brown" }
            .SelectMany(tone => new[] { "left", "middle", "right" }.SelectMany(part => Enumerable.Range(0, 4)
                .Select(frame => $"command-{level}-{tone}-{part}" + (frame == 0 ? "" : $"-{frame}"))))));
}
