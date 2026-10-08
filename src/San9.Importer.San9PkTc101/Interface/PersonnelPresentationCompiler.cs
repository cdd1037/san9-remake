using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>TC 1.0.1 dialogue slots and sound identities; see RE-0110/0111/0112.</summary>
internal static class PersonnelPresentationCompiler
{
    internal static PersonnelPresentationDefinition Compile()
    {
        Dictionary<string, DecisionMessageDefinition> messages = [];
        void Add(string role, int source, string? speaker, string? sound, params (string Slot, string Role)[] bindings) =>
            messages.Add(role, new($"messages.{source}", bindings.ToDictionary(p => p.Slot, p => p.Role), speaker, sound));
        (string, string)[] departure = [("person.0", "actor"), ("person.1", "ruler"), ("building.0", "place")];
        Add("move-start", 5658, "actor", null, departure);
        Add("search-start", 5669, "actor", null, departure);
        Add("employ-start", 5675, "actor", null, [.. departure, ("person.2", "target")]);
        Add("captive-start", 5778, "actor", null, [.. departure, ("person.2", "target")]);
        (string, string)[] searchAdvice = [("person.0", "adviser"), ("person.1", "ruler"), ("building.0", "place")];
        Add("search-favorable", 7297, "adviser", null, searchAdvice);
        Add("search-unfavorable", 7301, "adviser", null, searchAdvice);
        Add("search-first-visit", 7405, "adviser", null, searchAdvice);
        Add("search-return-visit", 7406, "adviser", null, searchAdvice);
        Add("employ-favorable", 7305, "adviser", null, ("person.0", "adviser"), ("person.1", "ruler"));
        Add("employ-unfavorable", 7309, "adviser", null, ("person.0", "adviser"), ("person.1", "ruler"));
        Add("search-none", 6345, "actor", null, ("person.0", "actor"), ("building.0", "place"), ("building.1", "home"));
        Add("search-empty", 6350, null, "decision-decrease", ("building.0", "place"), ("person.0", "actor"), ("force.0", "force"));
        Add("search-money", 6344, null, "decision-increase", ("building.0", "place"), ("person.0", "actor"), ("number.0", "money"));
        Add("search-treasure", 6343, null, "decision-success", ("building.0", "place"), ("person.0", "actor"), ("treasure.0", "treasure"));
        Add("employ-meeting", 10084, null, null, ("person.0", "actor"), ("person.1", "target"));
        Add("employ-secret-meeting", 10085, null, null, ("person.0", "actor"), ("person.1", "target"));
        (string, string)[] reply = [("person.0", "target"), ("person.1", "actor"), ("person.2", "ruler"), ("person.3", "old-ruler")];
        Add("employ-success", 6302, "target", null, reply);
        Add("employ-failure", 6318, "target", null, reply);
        Add("captive-success", 5552, "target", null, ("person.0", "target"), ("person.1", "ruler"), ("force.0", "force"));
        Add("captive-failure", 5478, "target", null, ("person.0", "target"), ("person.1", "actor"));
        Add("captive-free-failure", 5784, "target", null, ("person.0", "target"), ("person.1", "actor"));
        Add("employ-result-success", 9918, null, null, ("person.0", "actor"), ("person.1", "target"));
        Add("employ-result-failure", 9920, null, null, ("person.0", "actor"), ("person.1", "target"));
        Add("captive-result-success", 10046, null, null, ("person.0", "target"), ("force.0", "force"));
        Add("employ-unit", 10048, null, null, ("person.0", "target"), ("force.0", "force"), ("force.1", "old-force"));
        Add("employ-territory", 10050, null, null, ("building.0", "place"), ("force.0", "force"));
        Add("employ-city", 7111, null, "personnel-territory", ("building.0", "place"), ("force.0", "force"), ("force.1", "old-force"));
        messages["employ-city"] = messages["employ-city"] with { Illustration = "personnel-city-transfer" };
        return new(messages) { DepartureSound = "personnel-departure" };
    }
}
