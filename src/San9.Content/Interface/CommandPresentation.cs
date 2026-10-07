using San9.Content.Text;

namespace San9.Content.Interface;

public enum AdviceMetric { PublicOrder, Commerce, Durability }
public sealed record DomesticAdvice(string MessageKey, int SpeakerId, int[] Cities, AdviceMetric Metric,
    int Threshold, bool RequiresBarbarianThreat = false);
public sealed record PatrolPresentationDefinition(Dictionary<string, string> Messages, int CitizenId, DomesticAdvice[] Advice);
public sealed record FacilityPresentationDefinition(Dictionary<string, string> Messages, int? CitizenId = null);
public sealed record StrategistPresentationDefinition(string Appointed, string SuperiorSuccessor, string OtherSuccessor, string Result);
public sealed record ExilePresentationDefinition(int MessengerId, string Dismissal, string DismissalReply, string Release, string ReleaseReply, string Result, string ResultSound);
public sealed record TreasureTransferPresentationDefinition(string GrantDialogue, string GrantResult, string ConfiscateDialogue,
    string CaptiveDialogue, string ConfiscateResult, string LoyaltyIncrease, string LoyaltyDecrease, string HiddenLoyaltyDecrease,
    string IncreaseSound, string DecreaseSound);
public sealed record ExecutionPresentationDefinition(int MessengerId, string Announcement, string LastWords, string Result, string ResultSound);
public sealed record RewardPresentationDefinition(string Dialogue, string Result, string ResultSound);
public sealed record OfficePresentationDefinition(string Result, string DemotionNotice);

/// <summary>Authored presentation bindings. Source indices and file names are importer concerns.</summary>
public sealed record CommandPresentation(Dictionary<string, string> Sounds, PatrolPresentationDefinition? Patrol)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, FacilityPresentationDefinition> Facilities { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> Notices { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public StrategistPresentationDefinition? Strategist { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public RewardPresentationDefinition? Reward { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public ExilePresentationDefinition? Exile { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public TreasureTransferPresentationDefinition? TreasureTransfer { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public ExecutionPresentationDefinition? Execution { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public OfficePresentationDefinition? Offices { get; init; }

    public static CommandPresentation Empty => new([], null);
    public void Validate(TextCatalog texts, IEnumerable<int> people, IEnumerable<int> settlements)
    {
        var personIds = people.ToHashSet();
        if (Offices is { } offices)
        {
            if (!texts.Messages.ContainsKey(offices.Result) || !texts.Messages.ContainsKey(offices.DemotionNotice))
                throw new InvalidDataException("Missing office appointment presentation binding.");
            foreach (string role in new[] { "select", "select-office", "individual", "fill", "promote", "remove", "no-vacancy", "no-target", "accept", "no-changes", "cancel" })
                _ = texts.Resolve(AppointmentText.Key("rank", role));
        }
        if (Execution is { } execution)
        {
            if (!personIds.Contains(execution.MessengerId) || !Sounds.ContainsKey(execution.ResultSound) ||
                new[] { execution.Announcement, execution.LastWords, execution.Result }.Any(key => !texts.Messages.ContainsKey(key)))
                throw new InvalidDataException("Missing execution presentation binding.");
            foreach (string role in AppointmentText.Roles) _ = texts.Resolve(AppointmentText.Key("execute", role));
        }
        if (TreasureTransfer is { } transfer)
        {
            if (new[] { transfer.GrantDialogue, transfer.GrantResult, transfer.ConfiscateDialogue, transfer.CaptiveDialogue,
                    transfer.ConfiscateResult, transfer.LoyaltyIncrease, transfer.LoyaltyDecrease, transfer.HiddenLoyaltyDecrease }
                .Any(key => !texts.Messages.ContainsKey(key)) || !Sounds.ContainsKey(transfer.IncreaseSound) || !Sounds.ContainsKey(transfer.DecreaseSound))
                throw new InvalidDataException("Missing treasure transfer presentation binding.");
            foreach (string kind in new[] { "bestow", "confiscate" })
                foreach (string role in new[] { "select", "target", "accept", "cancel", "treasure", "select-treasure" })
                    _ = texts.Resolve(AppointmentText.Key(kind, role));
        }
        if (Exile is { } exile)
        {
            if (!personIds.Contains(exile.MessengerId) || !Sounds.ContainsKey(exile.ResultSound) ||
                new[] { exile.Dismissal, exile.DismissalReply, exile.Release, exile.ReleaseReply, exile.Result }
                    .Any(key => !texts.Messages.ContainsKey(key))) throw new InvalidDataException("Missing exile presentation binding.");
            foreach (string role in AppointmentText.Roles) _ = texts.Resolve(AppointmentText.Key("exile", role));
        }
        if (Reward is { } reward)
        {
            if (!texts.Messages.ContainsKey(reward.Dialogue) || !texts.Messages.ContainsKey(reward.Result) || !Sounds.ContainsKey(reward.ResultSound))
                throw new InvalidDataException("Missing reward presentation binding.");
            foreach (string role in AppointmentText.Roles) _ = texts.Resolve(AppointmentText.Key("reward", role));
        }
        if (Strategist is { } strategist)
        {
            if (new[] { strategist.Appointed, strategist.SuperiorSuccessor, strategist.OtherSuccessor, strategist.Result }
                .Any(key => !texts.Messages.ContainsKey(key))) throw new InvalidDataException("Missing strategist message.");
            foreach (string role in AppointmentText.Roles) _ = texts.Resolve(AppointmentText.Key("strategist", role));
        }
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
