using San9.Content.Text;

namespace San9.Content.Interface;

/// <summary>Presentation of committed world events, independent of their trigger and rule implementation.</summary>
public sealed record EventPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, int> People { get; init; } = [];
    public static readonly string[] RequiredRoles = [
        "founder-declaration", "force-founded", "city-independent", "officer-departed",
        "consolidation-arrival", "consolidation-offer", "consolidation-reply", "consolidation-result",
        "consolidation-trust", "consolidation-soldiers", "consolidation-morale", "force-eliminated",
        "ruler-death", "officer-death", "captive-death", "free-officer-death",
        "successor-declaration", "chained-prelude", "chained-coup", "chained-result",
        "chained-ruler-unavailable", "chained-conspirators-unavailable", "chained-court-unavailable", "three-visits-recommendation",
        "three-visits-first", "three-visits-recruitment", "three-visits-result", "three-visits-expired-reaction",
        "three-visits-expired", "guandu-introduction", "guandu-advice", "guandu-result",
        "guandu-soldiers", "guandu-food", "guandu-trust", "guandu-enemy-trust",
        "guandu-loyalty", "red-cliffs-introduction", "red-cliffs-result", "red-cliffs-soldiers",
        "red-cliffs-trust", "red-cliffs-enemy-trust", "red-cliffs-loyalty", "activation-introduction",
        "activation-reaction", "activation-result", "ceremony-introduction", "ceremony-result",
        "ceremony-leadership", "ceremony-war", "ceremony-intelligence", "ceremony-politics", "hanzhong-introduction", "hanzhong-result", "memorial-introduction",
        "memorial-result", "memorial-tactics", "five-tigers-introduction", "five-tigers-result",
        "five-tigers-loyalty", "five-tigers-tactics", "qiao-introduction", "qiao-result",
        "qiao-rival", "sun-marriage-introduction", "sun-marriage-result", "sun-marriage-relation",
        "sun-marriage-tactics", "force-trust", "force-loyalty", "emperor-first-death",
        "emperor-second-death", "visit-arrival", "visit-greeting", "visit-conversation",
        "visit-departure", "sage-loyalty", "diviner-advice", "physician-book",
        "treasure-acquired", "women-teaching", "tactic-learned", "healing-arrival",
        "healing-greeting", "healing-treatment", "healing-departure", "healing-result",
        "alliance-ended-record", "alliance-victory", "alliance-victory-declaration",
        "alliance-victory-trust", "alliance-victory-ended", "abdication-introduction", "abdication-emperor",
        "abdication-reply", "abdication-result", "proclamation-introduction", "proclamation-declaration",
        "proclamation-result", "investiture-introduction", "investiture-decree", "investiture-reply",
        "investiture-result", "promotion-introduction", "promotion-declaration", "promotion-result",
        "emperor-supported-dialogue", "emperor-supported", "emperor-deposed-dialogue", "emperor-deposed",
        "alliance-formed-image", "alliance-formed-declaration", "alliance-formed-trust", "alliance-formed-result",
        "alliance-formation-failed", "defection-declaration", "settlement-defection", "unit-defection",
        "defection-follower", "abandoned-defection", "alliance-leader-defeated", "alliance-leader-died",
        "alliance-insufficient", "alliance-expired", "alliance-target-defeated", "alliance-target-died",
        "alliance-betrayed", "alliance-leader-betrayed", "alliance-member-left", "alliance-member-ended", "alliance-betrayal-trust", "alliance-betrayal-loyalty",
        "melee-announcement", "sniping-announcement", "duel-announcement"];

    public void Validate(TextCatalog texts, IReadOnlyDictionary<string, string> sounds, IEnumerable<int> people)
    {
        if (RequiredRoles.Except(Messages.Keys).Any() || new[] { "emperor-first", "emperor-second", "messenger", "recommender" }.Except(People.Keys).Any()) throw new InvalidDataException("Incomplete world event presentation.");
        new DecisionPresentations([], People) { Shared = Messages }.Validate(texts, sounds, people);
        foreach (string role in new[] { "event-death", "event-founded", "event-consolidation", "force-eliminated-music", "event-court-music" })
            if (!sounds.ContainsKey(role)) throw new InvalidDataException($"Missing event sound: {role}.");
    }
}
