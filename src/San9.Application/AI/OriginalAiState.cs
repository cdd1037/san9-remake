using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public enum OriginalAiGroup { Unassigned, Favored, ReducedAttempts }
public sealed record OriginalAiForceState(int ForceId, long SchedulingKey, OriginalAiGroup Group);
public sealed record OriginalAiCorpsState(int CorpsId, OriginalAiMode Mode, int? TargetForceId, int? TargetSettlementId);
public sealed record OriginalAiState
{
    public const string CurrentVersion = "original-complete-v1";
    // Unstable boundaries cannot be saved. The immutable cursor is copied with the session transaction.
    [JsonIgnore] internal OriginalAiContinuation? Continuation { get; init; }
    [JsonRequired] public string Version { get; init; } = CurrentVersion;
    [JsonRequired] public OriginalAiDefinition Policy { get; init; } = OriginalAiDefinition.Default;
    [JsonRequired] public GameClock? PreparedClock { get; init; }
    [JsonRequired] public GameClock? RankedClock { get; init; }
    [JsonRequired] public ImmutableArray<OriginalAiForceState> Forces { get; init; } = [];
    /// <summary>Full rank order, highest first. Position is the stable snapshot rank, not current prestige.</summary>
    [JsonRequired] public ImmutableArray<int> DiplomaticOrder { get; init; } = [];
    [JsonRequired] public ImmutableArray<OriginalAiCorpsState> Corps { get; init; } = [];
    [JsonRequired] public long NextSchedulingKey { get; init; }

    internal static OriginalAiState Create(ScenarioInitialState content)
    {
        long next = content.Forces.Select(f => f.AutomaticSchedulingKey ?? -1).DefaultIfEmpty(-1).Max() + 1;
        var forces = content.Forces.OrderBy(f => f.Id).Select(f => new OriginalAiForceState(f.Id,
            f.AutomaticSchedulingKey ?? checked(next++), OriginalAiGroup.Unassigned)).ToImmutableArray();
        return new() { Policy = content.OriginalAi, Forces = forces, NextSchedulingKey = next };
    }

    internal OriginalAiState Reconcile(WorldSnapshot world)
    {
        long next = NextSchedulingKey;
        var known = Forces.Select(f => f.ForceId).ToHashSet();
        var corps = world.Corps.ToDictionary(c => c.Id);
        var forces = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated).Select(f => f.Id).ToHashSet();
        var places = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed).ToDictionary(s => s.Id);
        OriginalAiCorpsState ReconcileCorps(OriginalAiCorpsState state)
        {
            int owner = corps[state.CorpsId].OwnerId;
            bool invalidForce = state.TargetForceId is int target && (target == owner || !forces.Contains(target));
            bool invalidPlace = state.TargetSettlementId is int place &&
                (!places.TryGetValue(place, out var settlement) || settlement.OwnerId == owner);
            return invalidForce || invalidPlace ? new(state.CorpsId, OriginalAiMode.General, null, null) : state;
        }
        return this with
        {
            Forces = Forces.AddRange(world.Forces.Where(f => !known.Contains(f.Id))
                .Select(f => new OriginalAiForceState(f.Id, checked(next++), OriginalAiGroup.Unassigned))),
            NextSchedulingKey = next,
            Corps = Corps.Where(c => corps.ContainsKey(c.CorpsId)).Select(ReconcileCorps).ToImmutableArray(),
        };
    }

    internal void Validate(WorldSnapshot world, ScenarioInitialState content)
    {
        if (Version != CurrentVersion || Policy is null || Forces.IsDefault || Corps.IsDefault || DiplomaticOrder.IsDefault ||
            Forces.Select(f => f.ForceId).Distinct().Count() != Forces.Length || Forces.Select(f => f.SchedulingKey).Distinct().Count() != Forces.Length ||
            Forces.Any(f => !world.Forces.Any(w => w.Id == f.ForceId) || f.SchedulingKey < 0 || f.SchedulingKey >= NextSchedulingKey || !Enum.IsDefined(f.Group)) ||
            world.Forces.Any(f => !Forces.Any(w => w.ForceId == f.Id)) ||
            Corps.Select(c => c.CorpsId).Distinct().Count() != Corps.Length ||
            Corps.Any(c => !world.Corps.Any(w => w.Id == c.CorpsId) || !Enum.IsDefined(c.Mode) ||
                (c.Mode == OriginalAiMode.TargetForce) != (c.TargetForceId is not null) ||
                (c.Mode == OriginalAiMode.TargetSettlement) != (c.TargetSettlementId is not null) ||
                c.TargetForceId is int target && !world.Forces.Any(f => f.Id == target) ||
                c.TargetSettlementId is int home && !world.Settlements.Any(s => s.Id == home)) ||
            DiplomaticOrder.Distinct().Count() != DiplomaticOrder.Length || DiplomaticOrder.Any(id => !world.Forces.Any(f => f.Id == id)) ||
            PreparedClock is { } p && (!p.IsXunBoundary || p.DayNumber > world.Clock.DayNumber) ||
            RankedClock is { } r && (!r.IsXunBoundary || r.DayNumber > world.Clock.DayNumber) ||
            RankedClock is null && !DiplomaticOrder.IsEmpty)
            throw new ArgumentException("Invalid original AI state.");
        try
        {
            var people = content.Officers.Select(o => o.State.Id).ToHashSet();
            Policy.Validate(people, content.Military);
            if (Policy.Military is null) throw new InvalidDataException("Missing saved military policy.");
            Policy.Military.Validate(people, content.Military);
        }
        catch (InvalidDataException error) { throw new ArgumentException("Invalid saved original AI policy.", error); }
    }
}

internal sealed record OriginalAiContinuation(ControlSeat Seat, ImmutableArray<int> Queue, ImmutableArray<int> Seen,
    int WorkIndex, bool Prepared, ulong RandomState, ImmutableDictionary<int, long> Quotas,
    OriginalPrisonerProgress? Prisoners);

internal sealed class OriginalAiRandom
{
    private readonly Func<int, int> _next;
    internal OriginalAiRandom(StableRandom stream) : this(maximum => (int)(stream.NextUInt64() % (ulong)maximum)) { }
    internal OriginalAiRandom(Func<int, int> next) => _next = next;
    internal int Next(int exclusiveMaximum)
    {
        if (exclusiveMaximum < 2) return 0;
        int value = _next(exclusiveMaximum);
        return value >= 0 && value < exclusiveMaximum ? value : throw new InvalidOperationException("Planning draw is outside its range.");
    }
    internal bool Percent(int chance) => Next(100) < chance;
}

internal static class OriginalAiWorkGate
{
    internal static bool Calendar(GameClock clock, GameClock started, long key, OriginalAiWorkRule rule, bool advanced) =>
        clock.DayNumber - started.DayNumber >= (advanced ? rule.OpeningWaitDays / 2 : rule.OpeningWaitDays) &&
        (rule.PeriodDays <= 10 || ((clock.DayNumber % rule.PeriodDays) + key % rule.PeriodDays) % rule.PeriodDays < 10);

    internal static int Attempts(OriginalAiWorkRule rule, bool advanced, OriginalAiGroup group)
    {
        int coefficient = (advanced ? 3 : 2) - (group == OriginalAiGroup.ReducedAttempts ? 1 : 0);
        return (int)Math.Clamp((coefficient * (long)rule.MaximumAttempts + 1) / 3, 1, rule.MaximumAttempts);
    }
}
