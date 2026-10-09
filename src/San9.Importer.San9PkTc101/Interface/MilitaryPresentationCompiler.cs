using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class MilitaryPresentationCompiler
{
    // RE-SAN9PK-0137: every committed order speaks with the actual commander and SE index 19.
    internal static Dictionary<string, DecisionMessageDefinition> Compile()
    {
        Dictionary<string, DecisionMessageDefinition> messages = [];
        void Add(string role, int source, string? targetBinding = null, string target = "target")
        {
            Dictionary<string, string> bindings = new() { ["person.0"] = "commander", ["person.1"] = "recipient" };
            if (targetBinding is not null) bindings[targetBinding] = target;
            messages.Add(role, new($"messages.{source}", bindings, "commander", "military-order"));
        }
        Add("sortie", 5576);
        Add("reinforce", 5770, "building.0");
        Add("build", 5582, "building.0");
        Add("transport", 5792, "building.0");
        Add("transport-redirect", 5833, "building.0");
        Add("attack", 5839, "unit.0");
        Add("march", 5845, "building.0");
        Add("retreat", 5851, "building.0");
        Add("adjust", 5879, "unit.0", "unit");
        return messages;
    }
}
