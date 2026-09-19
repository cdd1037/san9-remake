using System.Collections.Immutable;

namespace San9.Core.Simulation;

/// <summary>Stable identity for an authored event consumption.</summary>
public readonly record struct PersistentEventKey
{
    public string Value { get; init; }

    public PersistentEventKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Event key cannot be blank.", nameof(value));
        Value = value;
    }
}

public enum EventConsumption
{
    None,
    OnSuccess,
    OnPresentation,
}

public enum EventTrigger
{
    Search,
    Xun,
    Month,
    Quarter,
    Year,
}

/// <summary>Candidate selected before any event effect is committed.</summary>
public sealed record EventCandidate
{
    public string Id { get; }
    public EventTrigger Trigger { get; }
    public int Priority { get; }
    public EventConsumption Consumption { get; }
    public PersistentEventKey? ConsumptionKey { get; }

    public EventCandidate(string id, EventTrigger trigger, int priority,
        EventConsumption consumption = EventConsumption.None, PersistentEventKey? consumptionKey = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Event id cannot be blank.", nameof(id));
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        if (!Enum.IsDefined(consumption)) throw new ArgumentOutOfRangeException(nameof(consumption));
        if (consumption == EventConsumption.None && consumptionKey is not null)
            throw new ArgumentException("A non-consuming event cannot have a consumption key.", nameof(consumptionKey));
        if (consumption != EventConsumption.None && consumptionKey is null)
            throw new ArgumentException("A consuming event requires a consumption key.", nameof(consumptionKey));
        if (consumptionKey is PersistentEventKey key && string.IsNullOrWhiteSpace(key.Value))
            throw new ArgumentException("Event key cannot be blank.", nameof(consumptionKey));
        Id = id;
        Trigger = trigger;
        Priority = priority;
        Consumption = consumption;
        ConsumptionKey = consumptionKey;
    }
}

public enum EventOutcomeKind
{
    Completed,
    Failed,
    Invalidated,
}

public enum EventResult
{
    Succeeded,
    Declined,
    Escaped,
    NoReward,
    Defeated,
}

/// <summary>Result of one atomic event submission.</summary>
public sealed record EventOutcome
{
    public string CandidateId { get; }
    public EventOutcomeKind Kind { get; }
    public bool ContinueSelection { get; }
    public EventResult? Result { get; }

    public EventOutcome(string candidateId, EventOutcomeKind kind, bool continueSelection,
        EventResult? result = null)
    {
        if (string.IsNullOrWhiteSpace(candidateId)) throw new ArgumentException("Candidate id cannot be blank.", nameof(candidateId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (result is EventResult value && !Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(result));
        if ((kind == EventOutcomeKind.Completed) != (result is not null))
            throw new ArgumentException("Only a completed submission has a presented result.", nameof(result));
        CandidateId = candidateId;
        Kind = kind;
        ContinueSelection = continueSelection;
        Result = result;
    }
}

/// <summary>Pure candidate gate used by every event producer before opening an activity session.</summary>
public static class EventSelection
{
    public static ImmutableArray<EventCandidate> Order(
        IEnumerable<EventCandidate> candidates,
        EventTrigger trigger,
        ISet<PersistentEventKey>? consumed = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        consumed ??= new HashSet<PersistentEventKey>();
        return candidates
            .Where(c => c.Trigger == trigger &&
                (c.ConsumptionKey is not PersistentEventKey key || !consumed.Contains(key)))
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static void Consume(EventCandidate candidate, EventOutcome outcome, ISet<PersistentEventKey> consumed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(consumed);
        if (!string.Equals(candidate.Id, outcome.CandidateId, StringComparison.Ordinal))
            throw new ArgumentException("Outcome does not belong to the candidate.", nameof(outcome));
        if (outcome.Kind != EventOutcomeKind.Completed || candidate.ConsumptionKey is not PersistentEventKey key) return;
        if (candidate.Consumption == EventConsumption.OnPresentation || outcome.Result == EventResult.Succeeded)
            consumed.Add(key);
    }

    public static ImmutableArray<PersistentEventKey> AddConsumedKey(
        ImmutableArray<PersistentEventKey> existing, EventCandidate candidate, EventOutcome outcome)
    {
        var set = existing.ToHashSet();
        Consume(candidate, outcome, set);
        return set.OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray();
    }
}
