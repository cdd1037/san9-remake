namespace San9.Content.Text;

public sealed record TextEntry(string Text, string SourceText);
public sealed record TextCatalog(int SchemaVersion, Dictionary<string, TextEntry> Entries)
{
    public Dictionary<string, MessageDefinition> Messages { get; init; } = [];
    public string Resolve(string key) => Entries.TryGetValue(key, out var entry)
        ? MessageMarkup.Plain(entry.Text) : throw new InvalidDataException($"Missing text: {key}");
    public string ResolveRich(string key) => Entries.TryGetValue(key, out var entry)
        ? entry.Text : throw new InvalidDataException($"Missing text: {key}");

    public void Validate()
    {
        MessageGraph.Validate(Messages);
        if (SchemaVersion != 2 || Entries.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
            string.IsNullOrWhiteSpace(p.Value.Text) || string.IsNullOrWhiteSpace(p.Value.SourceText)))
            throw new InvalidDataException("Invalid text catalog.");
    }
}
