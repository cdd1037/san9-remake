using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Interface;

internal static class EventPresentationCompiler
{
    private static readonly string[] Abilities = ["leadership", "war", "intelligence", "politics"];
    internal static void AddMessages(Dictionary<string, MessageDefinition> messages)
    {
        var source = messages["messages.9417"];
        var cases = source.Nodes.Take(3).OfType<MessageConditional>().ToArray();
        if (cases.Length != 3 || source.Nodes.Length <= 3) throw new InvalidDataException("Unconfirmed ability reward message layout.");
        for (int index = 0; index < Abilities.Length; index++)
        {
            // The source has three early-return cases; politics is its final fallthrough.
            var body = index == 3 ? source.Nodes.Skip(3).ToArray() : cases.Single(c => c.Predicate is { Operation: "=", Arguments: { Length: 2 } args } &&
                args[0] is { Operation: "binding", Binding: "number.0" } && args[1].Number == index).Body;
            messages.Add("presentation.events.ceremony-" + Abilities[index], new(body));
        }
    }
    internal static EventPresentationDefinition Compile(IReadOnlyList<San9ScenarioPersonRecord> people)
    {
        DecisionMessageDefinition M(int id, string? speaker = null, string? sound = null, string? image = null,
            params (string Slot, string Role)[] bindings) => new($"messages.{id}", bindings.ToDictionary(b => b.Slot, b => b.Role), speaker, sound, image);
        var messages = new Dictionary<string, DecisionMessageDefinition>()
        {
            ["founder-declaration"] = M(7179, "actor", bindings: [("person.0", "actor"), ("building.0", "place")]),
            ["force-founded"] = M(7181, sound: "event-founded", image: "event-founded", bindings: [("person.0", "actor"), ("building.0", "place")]),
            ["city-independent"] = M(7180, sound: "event-founded", image: "event-founded", bindings: [("person.0", "actor"), ("building.0", "place"), ("force.0", "previous-force")]),
            ["officer-departed"] = M(6140, sound: "decision-decrease", bindings: [("person.0", "actor"), ("force.0", "force")]),
            ["consolidation-arrival"] = M(6245, bindings: [("building.0", "place")]),
            ["consolidation-offer"] = M(6246, "actor", bindings: [("person.0", "actor"), ("person.1", "other")]),
            ["consolidation-reply"] = M(6253, "other", bindings: [("person.0", "other"), ("person.1", "actor")]),
            ["consolidation-result"] = M(6242, sound: "event-consolidation", image: "event-consolidation", bindings: [("force.0", "force"), ("force.1", "previous-force"), ("number.0", "year"), ("number.1", "month")]),
            ["consolidation-trust"] = M(9324, sound: "decision-increase", bindings: [("force.0", "force")]),
            ["consolidation-soldiers"] = M(6243, sound: "decision-increase", bindings: [("force.0", "force")]),
            ["consolidation-morale"] = M(6244, sound: "decision-increase", bindings: [("force.0", "force")]),
            ["force-eliminated"] = M(7194, image: "diplomacy-eliminated", bindings: [("force.0", "force"), ("number.0", "year"), ("number.1", "month")]),
            ["ruler-death"] = M(7192, sound: "event-death", image: "actor", bindings: [("person.0", "actor")]),
            ["officer-death"] = M(7193, sound: "event-death", image: "actor", bindings: [("person.0", "actor"), ("force.0", "force"), ("force.1", "corps")]),
            ["captive-death"] = M(7415, sound: "event-death", image: "actor", bindings: [("person.0", "actor"), ("force.0", "captor")]),
            ["free-officer-death"] = M(7416, sound: "event-death", image: "actor", bindings: [("person.0", "actor"), ("building.0", "place")]),
            ["successor-declaration"] = M(7185, "actor", bindings: [("person.0", "actor")])
        };
        void Add(string role, int id, string? speaker = null, string? sound = null, string? image = null,
            params (string Slot, string Role)[] bindings) => messages.Add(role, M(id, speaker, sound, image, bindings));
        const string notice = "decision-announcement", up = "decision-increase", down = "decision-decrease", good = "decision-success";
        Add("food-exhausted", 6260, bindings: [("force.0", "force")]);
        Add("food-exhausted-record", 9905, bindings: [("force.0", "force")]);
        Add("starvation-loss", 7113); Add("starvation-loss-record", 10028);
        Add("chained-prelude", 7520); Add("chained-coup", 7521); Add("chained-result", 9348, sound: notice);
        Add("chained-ruler-unavailable", 9349, sound: notice); Add("chained-conspirators-unavailable", 9350, sound: notice);
        Add("chained-court-unavailable", 9351, sound: notice, bindings: [("person.0", "emperor")]);
        Add("three-visits-recommendation", 9352, "actor");
        Add("three-visits-first", 7522); Add("three-visits-recruitment", 7523); Add("three-visits-result", 9353, sound: notice);
        Add("three-visits-expired-reaction", 9354, "actor"); Add("three-visits-expired", 9355, sound: notice);
        Add("guandu-introduction", 7524); Add("guandu-advice", 9377, "actor");
        Add("guandu-result", 9378, sound: notice); Add("guandu-soldiers", 9379, sound: good); Add("guandu-food", 9380, sound: good);
        Add("guandu-trust", 9381, sound: up); Add("guandu-enemy-trust", 9382, sound: up); Add("guandu-loyalty", 9383, sound: up);
        Add("red-cliffs-introduction", 7525); Add("red-cliffs-result", 9384, sound: notice, bindings: [("force.0", "force"), ("force.1", "previous-force")]);
        Add("red-cliffs-soldiers", 9385, sound: good, bindings: [("force.0", "previous-force")]);
        Add("red-cliffs-trust", 9387, sound: up, bindings: [("force.0", "force")]);
        Add("red-cliffs-enemy-trust", 9388, sound: up, bindings: [("force.0", "previous-force")]);
        Add("red-cliffs-loyalty", 9389, sound: up, bindings: [("force.0", "force")]);
        Add("activation-introduction", 7526); Add("activation-reaction", 9356, "actor"); Add("activation-result", 9357, sound: good);
        Add("ceremony-introduction", 7527); Add("ceremony-result", 9358, sound: notice, bindings: [("force.0", "force")]);
        foreach (string ability in Abilities)
            messages.Add("ceremony-" + ability, new("presentation.events.ceremony-" + ability, new()
            { ["person.0"] = "actor", ["number.1"] = "amount", ["number.2"] = "current" }, Sound: up));
        Add("hanzhong-introduction", 7528); Add("hanzhong-result", 9359, sound: notice);
        Add("memorial-introduction", 7530); Add("memorial-result", 9366, sound: notice); Add("memorial-tactics", 9367, sound: up);
        Add("five-tigers-introduction", 7531); Add("five-tigers-result", 9368, sound: notice, bindings: [("force.0", "force")]);
        Add("five-tigers-loyalty", 9369, sound: up, bindings: [("force.0", "force")]); Add("five-tigers-tactics", 9370, sound: up);
        Add("qiao-introduction", 7532); Add("qiao-result", 9371, sound: notice); Add("qiao-rival", 9373, sound: down);
        Add("sun-marriage-introduction", 7533); Add("sun-marriage-result", 9374, sound: notice);
        Add("sun-marriage-relation", 9375, sound: notice, bindings: [("force.0", "previous-force")]); Add("sun-marriage-tactics", 9376, sound: up);
        Add("force-trust", 9324, sound: up, bindings: [("force.0", "force")]); Add("force-loyalty", 9325, sound: up, bindings: [("force.0", "force")]);
        Add("emperor-first-death", 9346, sound: notice, image: "event-court"); Add("emperor-second-death", 9347, sound: notice, image: "event-court");
        Add("visit-arrival", 7859, image: "event-visit", bindings: [("person.0", "other"), ("person.1", "actor"), ("building.0", "place")]);
        Add("visit-greeting", 7860, "other", bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("visit-conversation", 7864, bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("visit-departure", 7865, "other", bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("sage-loyalty", 7869, sound: up, bindings: [("force.0", "force")]);
        Add("diviner-advice", 7870, "other", sound: good, bindings: [("person.0", "other")]);
        Add("physician-book", 7871, "other", bindings: [("person.0", "other")]);
        Add("treasure-acquired", 7178, sound: up, bindings: [("person.0", "actor"), ("string.0", "treasure-name")]);
        Add("women-teaching", 7872, "other", bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("tactic-learned", 7232, sound: up, bindings: [("person.0", "actor"), ("tactic.0", "tactic")]);
        Add("healing-arrival", 7873, bindings: [("person.0", "actor"), ("building.0", "place")]);
        Add("healing-greeting", 7874, "other", bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("healing-treatment", 7875, bindings: [("person.0", "actor")]);
        Add("healing-departure", 7876, "other", bindings: [("person.0", "other"), ("person.1", "actor")]);
        Add("healing-result", 7877, sound: up, bindings: [("force.0", "force")]);
        foreach (var (role, id) in new[] { ("leader-defeated", 9397), ("leader-died", 9398), ("insufficient", 9399), ("expired", 9400), ("target-defeated", 9401), ("target-died", 9402) })
            Add("alliance-" + role, id, bindings: id <= 9398 ? [("force.0", "leader-force"), ("force.1", "target-force")] : [("force.0", "target-force")]);
        Add("alliance-ended-record", 10104, bindings: [("force.0", "target-force")]);
        Add("alliance-victory", 9403, image: "event-alliance-victory", bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-victory-declaration", 9404, "actor", bindings: [("force.0", "target-force")]);
        Add("alliance-victory-trust", 9405, bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-victory-ended", 9406, bindings: [("force.0", "target-force")]);
        Add("abdication-introduction", 9333, bindings: [("force.0", "force"), ("person.0", "emperor")]);
        Add("abdication-emperor", 9334, "emperor", bindings: [("force.0", "force")]);
        Add("abdication-reply", 9335, "actor", bindings: [("person.0", "actor"), ("person.1", "emperor")]);
        Add("abdication-result", 9336, sound: notice, image: "event-court", bindings: [("force.0", "force"), ("person.0", "emperor")]);
        Add("proclamation-introduction", 9337, bindings: [("force.0", "force")]);
        Add("proclamation-declaration", 9338, "actor", bindings: [("person.0", "actor")]);
        Add("proclamation-result", 9339, sound: notice, image: "event-court", bindings: [("force.0", "force")]);
        Add("investiture-introduction", 9340, bindings: [("force.0", "force"), ("person.0", "emperor")]);
        Add("investiture-decree", 9341, "messenger", bindings: [("force.0", "force")]);
        Add("investiture-reply", 9413, "actor", bindings: [("person.0", "actor"), ("person.1", "emperor")]);
        Add("investiture-result", 9342, sound: notice, bindings: [("force.0", "force")]);
        Add("promotion-introduction", 9343, bindings: [("force.0", "force")]);
        Add("promotion-declaration", 9344, "actor", bindings: [("person.0", "actor"), ("force.0", "force")]);
        Add("promotion-result", 9345, sound: notice, bindings: [("force.0", "force")]);
        Add("emperor-supported-dialogue", 9327, "emperor", bindings: [("force.0", "force")]);
        Add("emperor-supported", 9328, sound: notice, bindings: [("force.0", "force"), ("person.0", "emperor")]);
        Add("emperor-deposed-dialogue", 9330, "emperor", bindings: [("number.0", "dynasty-years")]);
        Add("emperor-deposed", 9331, sound: notice, bindings: [("force.0", "force")]);
        Add("alliance-formed-image", 9391, image: "decision-alliance", bindings: [("force.0", "leader-force"), ("force.1", "target-force")]);
        Add("alliance-formed-declaration", 9392, "actor", bindings: [("force.0", "target-force")]);
        Add("alliance-formed-trust", 9393, bindings: [("force.0", "leader-force")]);
        Add("alliance-formed-result", 9395, bindings: [("force.0", "target-force")]);
        Add("alliance-formation-failed", 9396, bindings: [("force.0", "target-force")]);
        foreach (var (role, id) in new[] { ("melee-announcement", 10143), ("sniping-announcement", 10142), ("duel-announcement", 7167) })
            Add(role, id, sound: "event-casualty", bindings: [("person.0", "actor")]);
        Add("alliance-betrayed", 9407, bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-leader-betrayed", 9408, bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-member-left", 9409, bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-member-ended", 9410, bindings: [("force.0", "target-force"), ("force.1", "force")]);
        Add("alliance-betrayal-trust", 9411, sound: down, bindings: [("force.0", "force")]);
        Add("alliance-betrayal-loyalty", 9412, sound: down, bindings: [("force.0", "force")]);
        Add("defection-declaration", 6127, "actor", bindings: [("person.0", "actor"), ("force.0", "force")]);
        Add("settlement-defection", 6136, sound: "personnel-territory", image: "personnel-city-transfer", bindings: [("person.0", "actor"), ("force.0", "force")]);
        Add("unit-defection", 6175, sound: down, bindings: [("person.0", "actor"), ("force.0", "force")]);
        Add("defection-follower", 6176, sound: down, bindings: [("person.0", "actor"), ("force.0", "force")]);
        Add("abandoned-defection", 6177, sound: "personnel-territory", image: "personnel-city-transfer", bindings: [("building.0", "place"), ("force.0", "force")]);
        int Person(int biography) => people.Take(700).Select((p, id) => (p, id)).Single(p => p.p.BiographyResourceCode == biography).id;
        return new(messages)
        {
            People = new() { ["emperor-first"] = Person(563), ["emperor-second"] = Person(564), ["messenger"] = Person(669), ["recommender"] = Person(39) }
        };
    }
}
