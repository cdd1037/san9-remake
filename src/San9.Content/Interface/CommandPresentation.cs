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
public sealed record DemolitionPresentationDefinition(string Confirmation, string Dialogue, string Result, string ResultSound);
public sealed record OfficePresentationDefinition(string Result, string DemotionNotice);
public sealed record PersonnelPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages)
{
    public string DepartureSound { get; init; } = null!;
}

/// <summary>Authored presentation bindings. Source indices and file names are importer concerns.</summary>
public sealed record CommandPresentation(Dictionary<string, string> Sounds, PatrolPresentationDefinition? Patrol)
{
    [System.Text.Json.Serialization.JsonRequired]
    public BattlePresentationDefinition? Battle { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public MapAudioDefinition? MapAudio { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public DecisionPresentations Decisions { get; init; } = DecisionPresentations.Empty;
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

    [System.Text.Json.Serialization.JsonRequired]
    public PersonnelPresentationDefinition? Personnel { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, DecisionMessageDefinition> Military { get; init; } = [];

    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, DecisionMessageDefinition> Missions { get; init; } = [];

    [System.Text.Json.Serialization.JsonRequired]
    public ProgressPresentationDefinition? Progress { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public EventPresentationDefinition? Events { get; init; }

    [System.Text.Json.Serialization.JsonRequired]
    public DemolitionPresentationDefinition? Demolition { get; init; }

    public static CommandPresentation Empty => new([], null);
    public void Validate(TextCatalog texts, IEnumerable<int> people, IEnumerable<int> settlements)
    {
        var personIds = people.ToHashSet();
        Decisions.Validate(texts, Sounds, personIds);
        Events?.Validate(texts, Sounds, personIds);
        MapAudio?.Validate(Sounds);
        if (Progress is { } progress)
        {
            if (MapAudio is null) throw new InvalidDataException("Missing map audio presentation.");
            if (progress.OrderPrompt is null || string.IsNullOrWhiteSpace(progress.StrategyTitleSound) || !Sounds.ContainsKey(progress.StrategyTitleSound))
                throw new InvalidDataException("Incomplete strategy entry presentation.");
            if (ProgressPresentationDefinition.RecordRoles.Except(progress.Records.Keys).Any() ||
                ProgressPresentationDefinition.MapSpeechRoles.Except(progress.MapSpeeches.Keys).Any() ||
                ProgressPresentationDefinition.ContextRoles.Except(progress.Context.Keys).Any())
                throw new InvalidDataException("Incomplete progress presentation.");
            string[] roles = ["actor", "ruler", "other", "force", "enemy-force", "place", "other-place", "city", "unit", "treasure", "corps"];
            var messages = progress.Records.Values.Concat(progress.Context.Values).Concat(progress.MapSpeeches.Values).Append(progress.OrderPrompt);
            if (messages.Any(m => m.Bindings.Values.Except(roles).Any() || m.Speaker is { } speaker && !roles.Contains(speaker)))
                throw new InvalidDataException("Unknown progress presentation role.");
            new DecisionPresentations([], []) { Shared = progress.Records }.Validate(texts, Sounds, personIds);
            new DecisionPresentations([], []) { Shared = progress.Context }.Validate(texts, Sounds, personIds);
            new DecisionPresentations([], []) { Shared = progress.MapSpeeches }.Validate(texts, Sounds, personIds);
            new DecisionPresentations([], []) { Shared = new() { ["order-prompt"] = progress.OrderPrompt } }.Validate(texts, Sounds, personIds);
        }
        if (Missions.Count > 0)
        {
            string[] kinds = ["alienate", "burn", "seize", "rumor", "false-report", "disorder", "inspire", "rescue", "gift", "request", "return", "surrender"];
            string[] roles = ["actor", "ruler", "adviser", "target-person", "target-ruler", "target-adviser", "target-force", "enemy-force", "second-force", "place", "home", "force", "soldier", "defender", "corps", "year", "month", "previous-holder", "treasure"];
            var results = kinds.Where(k => k != "seize").SelectMany(k => new[] { k+"-success",k+"-failure" })
                .Concat(["seize-person", "seize-item", "seize-failure"]);
            if (kinds.Select(k => k + "-start").Concat(new[] { "strategy", "gift", "request", "return", "surrender" }
                    .SelectMany(k => new[] { k + "-favorable", k + "-unfavorable" })).Concat(results).Except(Missions.Keys).Any() ||
                Missions.Values.Any(m => m.Bindings.Values.Except(roles).Any() || m.Speaker is string speaker && !roles.Contains(speaker)))
                throw new InvalidDataException("Incomplete mission presentation.");
            string[] extra = ["corps-created", "corps-changed", "diplomacy-arrival", "diplomacy-greeting", "diplomacy-welcome",
                "gift-proposal", "surrender-proposal", "request-proposal", "request-unowned-proposal", "return-proposal", "diplomacy-host-adviser",
                "gift-host-favorable", "gift-host-unfavorable", "surrender-host-favorable", "surrender-host-unfavorable",
                "gift-accepted", "gift-refused", "surrender-accepted", "surrender-refused", "surrender-actor-accepted", "surrender-actor-refused", "surrender-eliminated",
                "false-report-action", "disorder-action", "inspire-action", "rescue-action", "burn-action", "seize-action", "seize-person-action", "seize-person-reaction", "seize-item-action", "seize-item-reaction",
                "strategy-reaction-united", "strategy-reaction-countered", "false-report-reaction-defensive", "disorder-reaction-defensive"];
            var reactions = new[] { "false-report", "disorder", "inspire", "rescue", "burn", "seize" }
                .SelectMany(k => new[] { k+"-reaction-success", k+"-reaction-failure" });
            if (extra.Concat(reactions).Except(Missions.Keys).Any() || !Decisions.People.ContainsKey("soldier") ||
                kinds.Any(k => string.IsNullOrWhiteSpace(Missions[k+"-start"].Sound)) ||
                !Sounds.ContainsKey("diplomacy-music") || !Sounds.ContainsKey("force-eliminated-music"))
                throw new InvalidDataException("Incomplete mission dialogue or departure sound.");
            new DecisionPresentations([], []) { Shared = Missions }.Validate(texts, Sounds, personIds);
        }
        if (Demolition is { } demolition)
        {
            new DecisionPresentations([], [])
            {
                Shared = new()
            {
                ["confirmation"] = new(demolition.Confirmation, new() { ["building.0"] = "place" }),
                ["dialogue"] = new(demolition.Dialogue, new() { ["building.0"] = "place", ["person.0"] = "actor" }, "actor"),
                ["result"] = new(demolition.Result, new() { ["building.0"] = "place", ["force.0"] = "force" }, Sound: demolition.ResultSound)
                }
            }.Validate(texts, Sounds, personIds);
            if (string.IsNullOrWhiteSpace(demolition.ResultSound))
                throw new InvalidDataException("Missing demolition result sound.");
        }
        if (Military.Count > 0)
        {
            string[] roles = ["sortie", "reinforce", "build", "transport", "transport-redirect", "attack", "march", "retreat", "adjust"];
            string[] values = ["commander", "recipient", "target", "unit"];
            if (roles.Except(Military.Keys).Any() || Military.Values.Any(m =>
                m.Speaker != "commander" || m.Illustration is not null || m.Bindings.Values.Except(values).Any()))
                throw new InvalidDataException("Invalid military command presentation.");
            new DecisionPresentations([], []) { Shared = Military }.Validate(texts, Sounds, personIds);
        }
        if (Personnel is {} personnel)
        {
            string[] roles = ["move-start", "search-start", "employ-start", "captive-start", "search-favorable", "search-unfavorable",
                "employ-favorable", "employ-unfavorable", "search-none", "search-empty", "search-money", "search-treasure",
                "employ-meeting", "employ-secret-meeting", "employ-success", "employ-failure", "captive-success", "captive-failure",
                "captive-free-failure", "employ-result-success", "employ-result-failure", "employ-unit", "employ-territory",
                "search-first-visit", "search-return-visit", "captive-result-success", "employ-city"];
            if (roles.Except(personnel.Messages.Keys).Any()) throw new InvalidDataException("Missing personnel presentation binding.");
            if (string.IsNullOrWhiteSpace(personnel.DepartureSound) || !Sounds.ContainsKey(personnel.DepartureSound))
                throw new InvalidDataException("Missing personnel departure sound.");
            new DecisionPresentations([], []) { Shared = personnel.Messages }.Validate(texts, Sounds, personIds);
            string[] values = ["actor", "ruler", "adviser", "target", "old-ruler", "place", "home", "force", "old-force", "treasure", "money"];
            if (personnel.Messages.Values.Any(m => m.Bindings.Values.Except(values).Any() ||
                m.Speaker is string speaker && speaker is not ("actor" or "target" or "adviser")))
                throw new InvalidDataException("Unknown personnel message role.");
        }
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
