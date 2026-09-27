using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed record GameplayMessageAudit(string Key, bool SourceConfirmed, bool RuntimeReady,
    string[] RequiredArguments);

/// <summary>Removes source identities and catalog sort encodings before messages enter the runtime package.</summary>
public sealed class GameplayMessageCompiler(ScenarioInitialState scenario, IReadOnlyList<San9ScenarioPersonRecord> people,
    int sourceScenarioId, int sourceScenarioMode)
{
    public Dictionary<string, MessageDefinition> Compile(IReadOnlyDictionary<string, MessageDefinition> source)
    {
        Dictionary<string, MessageDefinition> result = [];
        foreach (var (key, definition) in source)
        {
            MessageExpression Unsupported(string detail) => throw new InvalidDataException($"Message {key}: {detail}");
            MessageExpression Entity(string kind, int id) => new("entity", kind, id);
            MessageExpression Number(long value) => new("constant", Number: value);
            MessageExpression Property(string name, MessageExpression argument) => new("property", name, Arguments: [argument]);
            MessageExpression Query(string name, params MessageExpression[] arguments) => new("query", name, Arguments: arguments);
            MessageExpression PersonIdentity(MessageExpression subject, long biography)
            {
                int[] ids = scenario.Officers.Where(o => o.State.Id < people.Count && people[o.State.Id].BiographyResourceCode == biography)
                    .Select(o => o.State.Id).ToArray();
                if (ids.Length == 0) return Number(0);
                return ids.Select(id => new MessageExpression("=", Arguments: [subject, Entity("Person", id)]))
                    .Aggregate((left, right) => new("or", Arguments: [left, right]));
            }
            MessageExpression Translate(MessageExpression e)
            {
                var a = e.Arguments ?? [];
                if (e.Operation is "=" or "!" && a.Length == 2 && a[0] is { Operation: "binding", Binding: { } binding })
                {
                    var parts = binding.Split('.');
                    if (parts.Length == 3 && parts[0] == "unit" && parts[2] == "command-event-type" &&
                        a[1] is { Operation: "constant", Number: 37 or 52 })
                        return e with { Arguments = [new("binding", $"return-kind.{parts[1]}"),
                            new("text", (a[1].Number == 37 ? MessageReturnKind.Personnel : MessageReturnKind.Automatic).ToString())] };
                    if (parts.Length == 3 && parts[0] == "person" && parts[2] == "biography-resource" && a[1].Number is long biography)
                    {
                        var identity = PersonIdentity(new("binding", $"person.{parts[1]}"), biography);
                        return e.Operation == "=" ? identity : new("=", Arguments: [identity, Number(0)]);
                    }
                    if (parts.Length == 3 && parts[0] == "person" && parts[2] == "portrait" &&
                        a[1] is { Operation: "binding", Binding: { } other } && other.EndsWith(".portrait", StringComparison.Ordinal))
                    {
                        var same = Query("person.same-portrait", new("binding", $"person.{parts[1]}"), new("binding", other[..other.LastIndexOf('.')]));
                        return e.Operation == "=" ? same : new("=", Arguments: [same, Number(0)]);
                    }
                    if (parts.Length == 3 && parts[0] == "person" && parts[2] == "status" && a[1].Number is long status)
                    {
                        string? property = status switch { 0 => "is-ruler", 2 => "is-strategist", 4 => "is-captive", 5 => "is-free", _ => null };
                        if (property is null) return Unsupported($"Person status predicate {status} requires a semantic mapping.");
                        var predicate = Property("person." + property, new("binding", $"person.{parts[1]}"));
                        return e.Operation == "=" ? predicate : new("=", Arguments: [predicate, Number(0)]);
                    }
                    if (parts.Length == 3 && parts[0] == "force" && parts[2] == "id" && a[1].Number is long force)
                    {
                        if (!scenario.Forces.Any(f => f.Id == force)) return Number(e.Operation == "=" ? 0 : 1);
                        return e with { Arguments = [new("binding", $"force.{parts[1]}"), Entity("Force", checked((int)force))] };
                    }
                }
                if (e.Operation is "=" or "<=" && a.Length == 2 && a[0] is { Operation: "binding", Binding: { } rankBinding } &&
                    rankBinding.EndsWith(".peerage-rank", StringComparison.Ordinal) && a[1].Number is long rank)
                {
                    var definition = scenario.Information.Ranks.SingleOrDefault(r => r.Id == rank);
                    if (definition is null) return Unsupported($"Missing peerage definition {rank}.");
                    return new(e.Operation == "<=" ? ">=" : "=", Arguments:
                        [Property("force.rank-precedence", new("binding", rankBinding[..rankBinding.LastIndexOf('.')])), Number(definition.Precedence)]);
                }
                if (e.Operation == "binding")
                {
                    string[] parts = e.Binding!.Split('.');
                    if (parts.Length != 3) return e;
                    string group = parts[0], property = parts[2];
                    string? semantic = (group, property) switch
                    {
                        ("person", "name" or "surname" or "given-name" or "courtesy-name" or "office-name" or "office-command-capacity" or "location-name" or
                            "personality" or "righteousness" or "ambition" or "injury") => "person." + property,
                        ("person", "gender-flag") => "person.is-female", ("person", "gender-flag-clear") => "person.is-male",
                        ("person", "current-force-loyalty") => "person.loyalty",
                        ("person", "current-war") => "person.war", ("person", "current-intelligence") => "person.intelligence",
                        ("person", "current-leadership") => "person.leadership",
                        ("force", "name" or "country-name" or "rank-name" or "leader-name" or "direct-leader-name" or "is-alliance-leader") => "force." + property,
                        ("force", "same-root-corps-count") => "force.corps-count",
                        ("building", "name-with-type") => "settlement.name",
                        ("building", "name") => "settlement.base-name",
                        ("building", "owner-name" or "type-name") => "settlement." + property,
                        ("treasure", "name" or "category") => "treasure." + property,
                        ("tactic", "name" or "category-name") => "tactic." + property,
                        ("unit", "location-name" or "is-embedded" or "commander-name") => "unit." + property, _ => null
                    };
                    return semantic is null ? Unsupported($"No runtime property for {group}.{property}.") :
                        Property(semantic, new("binding", $"{group}.{parts[1]}"));
                }
                if (e.Operation == "query")
                {
                    if (e.Binding == "scenario") return Number(sourceScenarioId);
                    if (e.Binding == "scenario-mode") return Number(sourceScenarioMode == 1 ? 1 : 0);
                    if (e.Binding == "world-flag")
                    {
                        string? history = a is [{ Operation: "constant", Number: 2 }] ? "chained-plot.completed" :
                            a is [{ Operation: "constant", Number: 12 }] ? "qiao-marriage" : null;
                        return history is null ? Unsupported("World flag requires a named event history mapping.") :
                            Query("event.occurred", new MessageExpression("text", history));
                    }
                    if (e.Binding == "senior-kin")
                    {
                        // The source gives Liu Bei the elder form of address independently of genealogy.
                        return new("or", Arguments: [PersonIdentity(Translate(a[0]), 1), Query("senior-kin", a.Select(Translate).ToArray())]);
                    }
                    if (e.Binding == "person-in-internal-unit") return Property("person.is-stationed", Translate(a[0]));
                    if (e.Binding == "person-unit-command-in-38-52") return Property("person.is-on-military-duty", Translate(a[0]));
                    if (e.Binding == "has-treasure" && a is [_, { Operation: "constant", Number: >= 0 and <= int.MaxValue }])
                    {
                        int id = checked((int)a[1].Number!.Value);
                        return scenario.Information.Treasures.Any(t => t.Id == id)
                            ? Query("has-treasure", Translate(a[0]), Entity("Treasure", id)) : Number(0);
                    }
                    if (e.Binding is "parent-child" or "same-parent" or "same-bloodline" or "junior-kin" or "affinity-distance" or
                        "same-root-force" or "force-relation" or "year" or "month" or "day" or "season" or "has-treasure-category")
                        return e with { Arguments = a.Select(Translate).ToArray() };
                    return Unsupported($"No runtime query for {e.Binding}.");
                }
                return e with { Arguments = e.Arguments?.Select(Translate).ToArray() };
            }
            MessageNode[] Nodes(IEnumerable<MessageNode> nodes) => nodes.Select<MessageNode, MessageNode>(node => node switch
            {
                MessageStyled s => s with { Body = Nodes(s.Body) },
                MessageValue v => v with { Expression = Translate(v.Expression) },
                MessageConditional c => c with { Predicate = Translate(c.Predicate), Body = Nodes(c.Body) },
                MessageChoice c => c with { Index = Translate(c.Index), Choices = c.Choices.Select(Nodes).ToArray() },
                MessageRandomChoice c => c with { Choices = c.Choices.Select(Nodes).ToArray() }, _ => node
            }).ToArray();
            var nodes = MessageGraph.Simplify(Nodes(definition.Nodes));
            result.Add(key, new(nodes));
        }
        MessageGraph.Validate(result);
        return result;
    }

    public static GameplayMessageAudit[] Audit(IReadOnlyDictionary<string, MessageDefinition> messages) => messages.Select(pair =>
    {
        var requirements = MessageGraph.Requirements(pair.Key, messages);
        if (!requirements.ReferencesValid) throw new InvalidDataException($"Invalid or cyclic message references: {pair.Key}.");
        return new GameplayMessageAudit(pair.Key, true, true, requirements.Bindings);
    }).ToArray();
}
