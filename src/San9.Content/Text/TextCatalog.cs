namespace San9.Content.Text;

public sealed record TextCatalog(int SchemaVersion, Dictionary<string, string> Entries)
{
    public const int CurrentSchemaVersion = 6;
    public Dictionary<string, MessageDefinition> Messages { get; init; } = [];
    public Dictionary<int, MessagePersonProfile> MessagePeople { get; init; } = [];
    public string Resolve(string key) => Entries.TryGetValue(key, out var entry)
        ? MessageMarkup.Plain(entry) : throw new InvalidDataException($"Missing text: {key}");
    public string ResolveRich(string key) => Entries.TryGetValue(key, out var entry)
        ? entry : throw new InvalidDataException($"Missing text: {key}");

    public void Validate()
    {
        MessageGraph.Validate(Messages);
        if (SchemaVersion != CurrentSchemaVersion || MessagePeople.Any(p => p.Key < 0 || p.Value is null) || Entries.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
            string.IsNullOrWhiteSpace(p.Value)))
            throw new InvalidDataException("Invalid text catalog.");
        foreach (string entry in Entries.Values) MessageStyles.ValidateMarkup(entry);
        foreach (var person in MessagePeople.Values)
        foreach (string? key in new[] { person.SurnameKey, person.GivenNameKey })
            if (key is not null) _ = Resolve(key);
    }
}
