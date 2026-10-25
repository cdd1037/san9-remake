using San9.Content.Text;

namespace San9.Content.Interface;

public sealed record CapturePlacePresentation(string Illustration, bool UsesMusic);

/// <summary>Authored capture and third-party receipt scenes, with separate attacker and recipient identities.</summary>
public sealed record CapturePresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages,
    Dictionary<int, CapturePlacePresentation> Places, string VictoryMusic, string LossMusic, string CaptureSound)
{
    public void Validate(TextCatalog texts, IReadOnlyDictionary<string, string> sounds, IEnumerable<int> people)
    {
        if (Messages is null || Places is null || new[] { "won", "lost", "won-transfer", "lost-transfer" }.Except(Messages.Keys).Any() ||
            Places.Any(p => p.Key < 0 || p.Value is null || string.IsNullOrWhiteSpace(p.Value.Illustration)) ||
            new[] { VictoryMusic, LossMusic, CaptureSound }.Any(r => string.IsNullOrWhiteSpace(r) || !sounds.ContainsKey(r)))
            throw new InvalidDataException("Invalid capture presentation.");
        string[] roles = ["ruler", "actor", "place", "force", "previous-force"];
        if (Messages.Values.Any(m => m.Bindings.Values.Except(roles).Any() || m.Speaker is { } speaker && !roles.Contains(speaker)))
            throw new InvalidDataException("Unknown capture message role.");
        new DecisionPresentations([], []) { Shared = Messages }.Validate(texts, sounds, people);
    }

    public void ValidateReferences(IEnumerable<int> facilityTypes, IReadOnlyDictionary<string, string> illustrations)
    {
        if (!Places.Keys.ToHashSet().SetEquals(facilityTypes) || Places.Values.Any(p => !illustrations.ContainsKey(p.Illustration)))
            throw new InvalidDataException("Unknown capture facility type or illustration.");
    }
}
