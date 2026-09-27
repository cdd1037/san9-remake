using San9.Content.Text;

namespace San9.Content.Interface;

public enum AdviceMetric { PublicOrder, Commerce, Durability }
public sealed record DomesticAdvice(string MessageKey, int SpeakerId, int[] Cities, AdviceMetric Metric,
    int Threshold, bool RequiresBarbarianThreat = false);
public sealed record PatrolPresentationDefinition(Dictionary<string, string> Messages, int CitizenId, DomesticAdvice[] Advice);

/// <summary>Authored presentation bindings. Source indices and file names are importer concerns.</summary>
public sealed record CommandPresentation(Dictionary<string, string> Sounds, PatrolPresentationDefinition? Patrol)
{
    public static CommandPresentation Empty => new([], null);
    public void Validate(TextCatalog texts, IEnumerable<int> people, IEnumerable<int> settlements)
    {
        if (Patrol is not { } patrol) return;
        var ids = people.ToHashSet(); var cities = settlements.ToHashSet();
        foreach (string role in new[] { "normal", "cooperative", "citizen", "pair-first", "pair-second", "result" })
            if (!patrol.Messages.TryGetValue(role, out string? key) || !texts.Messages.ContainsKey(key))
                throw new InvalidDataException($"Missing patrol message: {role}.");
        if (!ids.Contains(patrol.CitizenId)) throw new InvalidDataException("Missing patrol citizen.");
        foreach (var advice in patrol.Advice)
            if (!ids.Contains(advice.SpeakerId) || !texts.Messages.ContainsKey(advice.MessageKey) ||
                advice.Cities.Except(cities).Any() || !Enum.IsDefined(advice.Metric) || advice.Threshold < 0)
                throw new InvalidDataException("Invalid domestic advice binding.");
        foreach (string role in new[] { "patrol-citizen", "domestic-result" })
            if (!Sounds.ContainsKey(role)) throw new InvalidDataException($"Missing sound: {role}.");
    }
}
