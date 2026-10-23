namespace San9.Content.Text;

public sealed record TextCatalog(int SchemaVersion, Dictionary<string, string> Entries)
{
    public const int CurrentSchemaVersion = 8;
    public const string ScenarioTitleKey = "scenario.title";
    public Dictionary<string, MessageDefinition> Messages { get; init; } = [];
    public Dictionary<int, MessagePersonProfile> MessagePeople { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public string[] MessageTags { get; init; } = [];
    public string Resolve(string key) => MessageMarkup.Plain(ResolveRich(key));
    public string ResolveRich(string key) => Entries.TryGetValue(key, out var entry) ? entry
        : Messages.TryGetValue(key, out var message) ? MessageMarkup.Static(message.Nodes)
        : throw new InvalidDataException($"Missing text: {key}");

    public void Validate()
    {
        MessageGraph.Validate(Messages);
        if (MessageTags.Any(string.IsNullOrWhiteSpace) || MessageTags.Distinct(StringComparer.Ordinal).Count() != MessageTags.Length)
            throw new InvalidDataException("Invalid message tags.");
        if (SchemaVersion != CurrentSchemaVersion || MessagePeople.Any(p => p.Key < 0 || p.Value is null) || Entries.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
            string.IsNullOrWhiteSpace(p.Value)))
            throw new InvalidDataException("Invalid text catalog.");
        foreach (string entry in Entries.Values) MessageStyles.ValidateMarkup(entry);
        foreach (var person in MessagePeople.Values)
        foreach (string? key in new[] { person.SurnameKey, person.GivenNameKey })
            if (key is not null) _ = Resolve(key);
    }
}
