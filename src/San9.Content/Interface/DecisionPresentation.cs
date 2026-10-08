using San9.Content.Text;

namespace San9.Content.Interface;

public enum DecisionWindowKind { Confirmation, VerticalChoice, PersonDisposition, PersonSelection, CountrySelection, Diplomacy }

/// <summary>Message inputs name semantic roles in the current encounter, never source slots or world identities.</summary>
public sealed record DecisionMessageDefinition(string Key, Dictionary<string, string> Bindings,
    string? Speaker = null, string? Sound = null, string? Illustration = null);

public sealed record DecisionPresentationDefinition(DecisionWindowKind Window,
    DecisionMessageDefinition Prompt, DecisionMessageDefinition[] Introduction,
    Dictionary<string, DecisionMessageDefinition[]> Results, Dictionary<string, string> Options)
{
    public DecisionMessageDefinition? SelectionConfirmation { get; init; }
    public DecisionMessageDefinition? CancelConfirmation { get; init; }
}

public sealed record DecisionPresentations(Dictionary<string, DecisionPresentationDefinition> Events,
    Dictionary<string, int> People)
{
    public Dictionary<string, string> Subjects { get; init; } = [];
    public Dictionary<string, DecisionMessageDefinition> Shared { get; init; } = [];
    public static DecisionPresentations Empty => new([], []);

    public void Validate(TextCatalog texts, IReadOnlyDictionary<string, string> sounds, IEnumerable<int> people)
    {
        var ids = people.ToHashSet();
        if (People.Any(p => string.IsNullOrWhiteSpace(p.Key) || !ids.Contains(p.Value)))
            throw new InvalidDataException("Invalid decision speaker.");
        if (Subjects.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
            throw new InvalidDataException("Invalid decision subject.");
        foreach (var (role, definition) in Events)
        {
            if (string.IsNullOrWhiteSpace(role) || !Enum.IsDefined(definition.Window) || definition.Options.Count == 0)
                throw new InvalidDataException("Invalid decision presentation.");
            foreach (var message in definition.Introduction.Append(definition.Prompt).Concat(definition.Results.Values.SelectMany(v => v))
                .Concat(new[] { definition.SelectionConfirmation, definition.CancelConfirmation }.OfType<DecisionMessageDefinition>()))
            {
                ValidateMessage(role, message);
            }
        }
        foreach (var (role, message) in Shared) ValidateMessage(role, message);
        void ValidateMessage(string role, DecisionMessageDefinition message)
        {
            var requirements = MessageGraph.Requirements(message.Key, texts.Messages);
            if (!requirements.ReferencesValid || requirements.Bindings.Except(message.Bindings.Keys).Any() ||
                message.Bindings.Values.Any(string.IsNullOrWhiteSpace) || message.Sound is string sound && !sounds.ContainsKey(sound))
                throw new InvalidDataException($"Incomplete decision message: {role}/{message.Key}.");
        }
    }
}
