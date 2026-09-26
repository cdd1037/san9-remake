namespace San9.Content.Interface;

/// <summary>Semantic art and text roles; source resource numbers belong to the importer.</summary>
public static class CommandMenuSkin
{
    public static IReadOnlyList<string> EntryIds { get; } = Array.AsReadOnly(new[] {
        "facilities", "military", "talent", "strategy", "diplomacy", "appointments", "patrol", "commerce", "cultivate", "repair", "recruit", "train", "buy", "sell", "demolish", "sortie", "build", "transport", "summon", "move", "search", "employ", "alienate", "burn", "seize", "rumor", "false-report", "disorder", "inspire", "rescue", "gift", "request", "return", "surrender", "delegate", "strategist", "rank", "reward", "bestow", "confiscate", "execute", "exile", "attack", "march", "redirect-transport"
    });
    public static string DescriptionKey(string id) => $"ui.command.{id}";
    public static IEnumerable<string> RequiredRoles => EntryIds.Select(id => $"command-illustration-{id}")
        .Concat(new[] { "top", "middle", "bottom", "illustration-frame", "condition", "summary-city", "summary-facility", "summary-unit", "summary-merchant", "summary-training" }.Select(id => "command-" + id))
        .Concat(new[] { "primary", "secondary" }.SelectMany(level => new[] { "blue", "green", "brown" }
            .SelectMany(tone => new[] { "left", "middle", "right" }.SelectMany(part => Enumerable.Range(0, 4)
                .Select(frame => $"command-{level}-{tone}-{part}" + (frame == 0 ? "" : $"-{frame}"))))));
}
