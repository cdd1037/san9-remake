namespace San9.Content.Text;

public sealed record TextEntry(string Text, string SourceText);
public sealed record TextCatalog(int SchemaVersion, Dictionary<string, TextEntry> Entries)
{
    public const int CurrentSchemaVersion = 4;
    public Dictionary<string, MessageDefinition> Messages { get; init; } = [];
    public Dictionary<int, MessagePersonProfile> MessagePeople { get; init; } = [];
    public string Resolve(string key) => Entries.TryGetValue(key, out var entry)
        ? MessageMarkup.Plain(entry.Text) : throw new InvalidDataException($"Missing text: {key}");
    public string ResolveRich(string key) => Entries.TryGetValue(key, out var entry)
        ? entry.Text : throw new InvalidDataException($"Missing text: {key}");

    public void Validate()
    {
        MessageGraph.Validate(Messages);
        if (SchemaVersion != CurrentSchemaVersion || MessagePeople.Any(p => p.Key < 0 || p.Value is null) || Entries.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
            string.IsNullOrWhiteSpace(p.Value.Text) || string.IsNullOrWhiteSpace(p.Value.SourceText)))
            throw new InvalidDataException("Invalid text catalog.");
        foreach (var person in MessagePeople.Values)
        foreach (string? key in new[] { person.SurnameKey, person.GivenNameKey })
            if (key is not null) _ = Resolve(key);
    }
}
