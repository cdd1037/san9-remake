using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class OutcomePresentationCompiler
{
    internal static void Add(ProgressPresentationDefinition content)
    {
        static DecisionMessageDefinition M(int id, string? speaker = null, string? sound = null,
            params (string Slot, string Role)[] bindings) => new($"messages.{id}", bindings.ToDictionary(b => b.Slot, b => b.Role), speaker, sound);
        void Record(string role, int id, params (string Slot, string Role)[] bindings) => content.Records.Add(role, M(id, bindings: bindings));
        void Prompt(string role, int id, string? speaker = null, string? sound = null,
            params (string Slot, string Role)[] bindings) => content.Feedback.Add(role, M(id, speaker, sound, bindings));
        (string, string)[] tactic = [("person.0", "actor"), ("tactic.0", "tactic")];
        Record("tactic-learned", 9997, tactic); Record("tactic-learned-other", 9998, tactic);
        Prompt("tactic-learned", 7232, sound: "decision-increase", bindings: tactic);
        (string, string)[] injury = [("person.0", "actor"), ("string.0", "injury")];
        Record("injury-worsened", 9939, injury); Prompt("injury-worsened", 7174, sound: "decision-decrease", bindings: injury);
        foreach (var (role, record, prompt) in new[] { ("unit-commander-changed",10140,7508), ("place-commander-changed",10066,7444), ("corps-leader-changed",10067,7191) })
        {
            (string, string)[] bindings = role == "place-commander-changed" ? [("person.0", "actor"), ("unit.0", "unit")] : [("person.0", "actor")];
            Record(role, record, bindings); Prompt(role, prompt, bindings: bindings);
        }
        Record("corps-reorganized", 9991, ("force.0", "force")); Prompt("corps-reorganized", 7182, bindings: [("force.0", "force")]);
        Record("high-spirits-expired", 10006, ("person.0", "actor"));
        Record("morale-lost", 9957, ("unit.0", "unit"), ("number.0", "current"));
        Record("unit-morale-collapsed", 9983, ("unit.0", "unit"));
        Prompt("appearance-introduction", 6224, bindings: [("person.0", "ruler"), ("person.1", "actor")]);
        Prompt("appearance-greeting", 6141, "actor", bindings: [("person.0", "actor")]);
        Prompt("appearance-result", 6146, sound: "decision-announcement", bindings: [("person.0", "actor"), ("person.1", "ruler")]);
        Record("treasury-captured", 9996, ("force.0", "force"), ("number.0", "amount"), ("number.1", "food"));
        Record("treasury-lost", 10089, ("force.0", "enemy-force"), ("number.0", "amount"), ("number.1", "food"));
        Prompt("treasury-captured", 7196, bindings: [("force.0", "force"), ("force.1", "enemy-force"), ("number.0", "amount"), ("number.1", "food")]);
        Prompt("prisoner-escape-speech", 6179, "actor", "decision-announcement", ("person.0", "actor"));
        Prompt("prisoner-escape-result", 6225, bindings: [("person.0", "actor"), ("building.0", "place")]);
        Record("battle-captured", 10022, ("unit.0", "unit"), ("force.0", "force"), ("person.0", "actor"), ("number.0", "count"));
        Record("battle-persons-lost", 10086, ("force.0", "force"), ("person.0", "actor"), ("number.0", "count"));
        Record("battle-persons-retreated", 10068, ("person.0", "actor"), ("number.0", "count"));
        Prompt("battle-capture-speech", 11114, "actor", bindings: [("person.0", "actor"), ("person.1", "ruler")]);
        Prompt("battle-capture-dialogue", 7197, "actor", bindings: [("person.0", "actor"), ("person.1", "ruler")]);
        Record("burn-victim-success", 9945, ("building.0", "place"));
        Record("burn-victim-failure", 9949, ("person.0", "actor"), ("building.0", "place"));
        Record("burn-food", 9985, ("force.0", "enemy-force"), ("number.0", "current"), ("number.1", "amount"));
        Record("alienate-victim-failure", 9951, ("person.0", "actor"), ("person.1", "other"));
        Record("loyalty-lost", 9989, ("person.0", "other"), ("number.0", "current"));
        Record("loyalty-hidden", 9950, ("person.0", "other"), ("person.1", "ruler"));
        Record("rumor-observer-success", 9940, ("person.0", "actor"), ("force.0", "enemy-force"));
        Record("rumor-observer-failure", 9942, ("person.0", "actor"), ("force.0", "enemy-force"));
        Record("seize-money-victim", 10091, ("number.0", "current"), ("number.1", "amount"));
        Record("seize-money-other", 9936, ("force.0", "enemy-force"));
        Record("seize-item-victim", 9930, ("person.0", "other"), ("treasure.0", "treasure"));
        Record("seize-item-other", 9931, ("person.0", "other"), ("treasure.0", "treasure"));
        Record("seize-person-victim", 9933, ("person.0", "other")); Record("seize-person-other", 9934, ("person.0", "other"));
        Record("false-report-victim-blocked", 9927, ("person.0", "other"), ("force.0", "force"));
        Record("disorder-victim-success", 9964, ("person.1", "other"));
        Record("disorder-victim-blocked", 10118, ("person.0", "other"), ("force.0", "force"));
        Record("disorder-other-success", 9965, ("person.0", "other"), ("force.0", "enemy-force"));
        Record("inspire-other-success", 9954, ("person.0", "other"), ("force.0", "enemy-force"));
        Record("rescue-other-result", 9959, ("person.0", "other"), ("force.0", "enemy-force"));
        Record("morale-gained", 9955, ("unit.0", "unit"), ("number.0", "current"));
        Record("reinforcement-arrived", 9960, ("person.0", "other"), ("number.0", "current"));
        foreach (var (role, record, speech) in new[] { ("policy-construction-aborted",10071,10304), ("policy-transport-aborted",10076,10313),
            ("policy-return-target-changed",10072,10322), ("policy-enter-captured-place",10079,10331),
            ("policy-return-after-action",10080,11294), ("policy-attack-unit-after-action",10081,11303), ("policy-attack-place-after-action",10082,11312) })
        {
            var records = new Dictionary<string, string> { ["person.0"] = "actor", ["person.1"] = "other", ["building.0"] = "place" };
            var speeches = new Dictionary<string, string>(records) { ["building.1"] = "other-place" };
            if (role is "policy-construction-aborted" or "policy-transport-aborted" or "policy-return-target-changed")
            { speeches["building.0"] = "previous-place"; speeches["building.1"] = "place"; }
            content.Records.Add(role, new($"messages.{record}", records));
            content.MapSpeeches.Add(role, new($"messages.{speech}", speeches, "actor"));
        }
    }
}
