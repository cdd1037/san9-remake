using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>Shared diplomatic speeches, independent of whether a player response is pending.</summary>
internal static class DiplomacyConversationCompiler
{
    internal static Dictionary<string, DecisionMessageDefinition> Compile()
    {
        Dictionary<string, DecisionMessageDefinition> messages = [];
        foreach (var (role, sources) in new[]
        {
            ("attack-request", new[] { 6421, 6434, 6447, 6460, 6473 }),
            ("prisoner-return", new[] { 6493, 6506, 6519, 6532, 6545 })
        })
        {
            string[] kinds = ["free", "prisoner", "money", "treasure", "refuse"];
            for (int i = 0; i < kinds.Length; i++)
                messages[role + "-quote-" + kinds[i]] = new($"messages.{sources[i]}", new()
                {
                    ["person.0"] = "host", ["person.1"] = "actor", ["person.2"] = kinds[i] == "prisoner" ? "exchange-person" : "other",
                    ["force.0"] = "target-force", ["force.1"] = "requester-force", ["building.0"] = "place",
                    ["number.0"] = "cost", ["treasure.0"] = "exchange-treasure"
                }, "host");
            foreach (var (answer, source) in new[] { ("thanks", 6376), ("accept", 6385), ("decline", 6394), ("refused", 6398) })
                messages[role + "-" + answer] = new($"messages.{source}", new()
                {
                    ["person.0"] = "actor", ["person.1"] = "host", ["person.2"] = "requester-ruler"
                }, "actor");
        }
        foreach (var (role, yes, no) in new[] { ("gift", 7361, 7365), ("surrender", 7377, 7381) })
            foreach (var (suffix, source) in new[] { ("favorable", yes), ("unfavorable", no) })
                messages[role + "-host-" + suffix] = new($"messages.{source}", new()
                { ["person.0"] = "adviser", ["person.1"] = "host" }, "adviser");
        foreach (var (answer, source) in new[] { ("accepted", 6376), ("refused", 6398) })
            messages["gift-actor-" + answer] = new($"messages.{source}", new()
            { ["person.0"] = "actor", ["person.1"] = "host", ["person.2"] = "requester-ruler" }, "actor");
        return messages;
    }
}
