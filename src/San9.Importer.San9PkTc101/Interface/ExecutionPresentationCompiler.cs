using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class ExecutionPresentationCompiler
{
    internal static ProgressPresentationDefinition Compile()
    {
        DecisionMessageDefinition M(int source, string? speaker = null, params (string Slot, string Role)[] values) =>
            new($"messages.{source}", values.ToDictionary(v => v.Slot, v => v.Role), speaker);
        Dictionary<string, DecisionMessageDefinition> records = new()
        {
            ["unit-contact"] = M(9906, values: [("person.0", "actor")]),
            ["settlement-contact"] = M(9908, values: [("building.0", "place")]),
            ["waypoint-reached"] = M(10144, values: [("unit.0", "unit")]),
            ["unit-lost-other"] = M(9994, values: [("unit.0", "unit"), ("force.0", "enemy-force")]),
            ["territory-changed-other"] = M(10120, values: [("force.0", "force"), ("force.1", "enemy-force"), ("building.0", "place")]),
            ["facility-destroyed-other"] = M(9916, values: [("building.0", "place")]),
            ["unit-lost"] = M(9993, values: [("unit.0", "unit")]),
            ["unit-defeated"] = M(10097, values: [("unit.0", "unit"), ("person.0", "other"), ("force.0", "enemy-force")]),
            ["settlement-won"] = M(10095, values: [("person.0", "actor"), ("building.0", "place")]),
            ["settlement-lost"] = M(10093, values: [("person.0", "actor"), ("building.0", "place"), ("force.0", "force")]),
            ["territory-changed"] = M(10096, values: [("building.0", "place"), ("force.0", "force")]),
            ["territory-lost"] = M(10094, values: [("building.0", "place"), ("force.0", "force")]),
            ["facility-won"] = M(9914, values: [("person.0", "actor"), ("building.0", "place")]),
            ["facility-destroyed"] = M(9915, values: [("building.0", "place")]),
            ["construction-completed"] = M(9911, values: [("building.0", "place")]),
            ["transport-completed"] = M(10029, values: [("person.0", "actor"), ("building.0", "place")]),
            ["person-captured"] = M(9977, values: [("person.0", "actor"), ("force.0", "force")]),
            ["enemy-captured"] = M(10116, values: [("person.0", "actor")]),
            ["prisoner-escaped"] = M(6225, values: [("person.0", "actor"), ("building.0", "place")]),
            ["ruler-succeeded"] = M(9992, values: [("person.0", "actor"), ("person.1", "other")]),
            ["force-eliminated"] = M(10112, values: [("force.0", "force")]),
            ["ruler-death"] = M(7192, values: [("person.0", "actor")]),
            ["natural-death"] = M(7193, values: [("person.0", "actor"), ("force.0", "force"), ("force.1", "corps")]),
            ["captive-death"] = M(7415, values: [("person.0", "actor"), ("force.0", "force")]),
            ["melee-death"] = M(10143, values: [("person.0", "actor")]),
            ["sniping-death"] = M(10142, values: [("person.0", "actor")]),
            ["duel-death"] = M(7167, values: [("person.0", "actor")])
        };
        Dictionary<string, DecisionMessageDefinition> context = [];
        void Dialogue(string role, int source, params (string Slot, string Role)[] extra) => context.Add(role,
            M(source, "actor", new[] { ("person.0", "actor"), ("person.1", "ruler") }.Concat(extra).ToArray()));
        Dialogue("enemy-city", 7293, ("force.0", "enemy-force"), ("building.0", "place"));
        Dialogue("enemy-place", 7436, ("force.0", "enemy-force"), ("building.0", "place"), ("building.1", "city"));
        Dialogue("food-shortage", 7498);
        Dialogue("money-shortage", 7502);
        Dialogue("reinforce-from", 7490, ("building.0","other-place"), ("building.1","place"), ("force.0","force"));
        Dialogue("reinforce-to", 7494, ("building.0","other-place"), ("building.1","place"), ("force.0","force"));
        foreach (var (role, source) in new[] { ("desperate",7450), ("rockfall",7455), ("siege",7474),
                     ("city",7463), ("place",7470), ("counterattack",7482) })
            Dialogue("defense-" + role, source, ("building.0", "place"));
        Dialogue("person-city",7239,("person.2","other"),("building.0","place"));
        Dialogue("person-place",7417,("person.2","other"),("building.0","place"),("building.1","city"));
        Dialogue("person-city-anonymous",7243,("building.0","place"));
        Dialogue("person-place-anonymous",7421,("building.0","place"),("building.1","city"));
        Dialogue("person-region",7247,("building.0","city"));
        Dialogue("treasure-city",7251,("building.0","place"),("treasure.0","treasure"));
        Dialogue("treasure-place",7425,("building.0","place"),("building.1","city"),("treasure.0","treasure"));
        Dialogue("treasure-city-anonymous",7258,("building.0","place"));
        Dialogue("treasure-place-anonymous",7432,("building.0","place"),("building.1","city"));
        Dialogue("treasure-region",7262,("building.0","city"));
        Dialogue("ruler-health",7266);
        Dialogue("adviser-health",7270);
        Dialogue("officer-health",7274,("person.2","other"));
        return new(records, context)
        {
            MapSpeeches = new()
            {
                ["waypoint-reached"] = M(11348, "actor", ("person.0", "actor")),
                ["contact-attack"] = M(10441, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-defense"] = M(10508, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-attack-strong"] = M(10419, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-attack-weak"] = M(10430, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-defense-strong"] = M(10497, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-defense-weak"] = M(10488, "actor", ("person.0", "actor"), ("person.1", "other")),
                ["contact-building"] = M(10452, "actor", ("person.0", "actor"), ("person.1", "other"))
            },
            OrderPrompt = new("messages.5992", new() { ["person.0"] = "ruler" }, Sound: "strategy-order-prompt"),
            StrategyTitleSound = "strategy-title"
        };
    }
}
