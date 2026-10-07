using San9.Content.Text;

namespace San9.Content.Interface;

public enum AdviceMetric { PublicOrder, Commerce, Durability }
public sealed record DomesticAdvice(string MessageKey, int SpeakerId, int[] Cities, AdviceMetric Metric,
    int Threshold, bool RequiresBarbarianThreat = false);
public sealed record PatrolPresentationDefinition(Dictionary<string, string> Messages, int CitizenId, DomesticAdvice[] Advice);
public sealed record FacilityPresentationDefinition(Dictionary<string, string> Messages, int? CitizenId = null);

/// <summary>Authored presentation bindings. Source indices and file names are importer concerns.</summary>
public sealed record CommandPresentation(Dictionary<string, string> Sounds, PatrolPresentationDefinition? Patrol)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, FacilityPresentationDefinition> Facilities { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> Notices { get; init; } = [];

    public static CommandPresentation Empty => new([], null);
    public void Validate(TextCatalog texts, IEnumerable<int> people, IEnumerable<int> settlements)
    {
        var personIds = people.ToHashSet();
        foreach (var (kind, definition) in Facilities)
        {
            bool combination = kind is "commerce" or "cultivate" or "repair" or "train";
            if (!combination && kind is not ("recruit" or "buy" or "sell")) throw new InvalidDataException("Unknown facility presentation.");
            string[] required = combination ? ["normal","cooperative","citizen","pair-first","pair-second","result"] : ["normal","result"];
            if (kind == "repair") required = [.. required,"complete","complete-dialogue"];
            if (required.Any(role => !definition.Messages.ContainsKey(role)) || definition.Messages.Values.Any(key => !texts.Messages.ContainsKey(key)) ||
                combination && (definition.CitizenId is not int citizen || !personIds.Contains(citizen)))
                throw new InvalidDataException($"Invalid facility presentation: {kind}.");
            string[] sounds = combination ? ["facility-result","patrol-citizen"] : kind == "recruit" ? ["recruit-result"] : ["facility-result","trade-start"];
            if (kind == "repair") sounds = [.. sounds,"repair-complete"];
            if (sounds.Any(role => !Sounds.ContainsKey(role))) throw new InvalidDataException($"Missing facility sound: {kind}.");
        }
        foreach (var (role, key) in Notices)
            if (!texts.Messages.ContainsKey(key))
                throw new InvalidDataException($"Missing notice message: {role}.");
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
