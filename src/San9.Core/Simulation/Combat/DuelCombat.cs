using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ChooseDuelSuccessorCommand(Guid CommandId, int IssuerId, long Sequence, int RulerId, [property: JsonRequired] int? SuccessorId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record DuelSuccessionPrompt(int RulerId, int RespondingForceId, ImmutableArray<int> CandidateIds);
// UsedUnits and UsedBuildings contain initiators, never actors that have only retaliated.
public sealed record CombatDayContinuation(ImmutableArray<int> RemainingUnits, ImmutableArray<int> UsedUnits,
    ImmutableArray<int> UsedBuildings)
{
    public ImmutableArray<int>? RemainingBuildings { get; init; }
}
public sealed record DuelCombatDecision(GameClock XunStarted, CombatDayContinuation Day, CombatExchangeState Exchange,
    ImmutableArray<int> AffectedForces, bool Counter, bool TacticReady,
    DuelAftermathProgress Aftermath, int RespondingForceId);

internal static class DuelCombat
{
    internal static (DuelAftermathProgress Progress, bool TacticReady)? TryOpportunity(WorldMutation world,
        int sourceId, int targetId, SimulationRules rules, List<DomainEvent> events, bool counter, CombatExchange exchange)
    {
        var order = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = targetId };
        bool? tacticReady = TacticCombat.PrepareOpportunity(world, sourceId, order, rules, events, counter, exchange);
        if (tacticReady is null) return null;
        var roster = DuelParticipants.Select(world, world.Units[sourceId], world.Units[targetId], rules.Military!);
        if (roster is not null)
        {
            var outcome = DuelContest.Resolve(world, roster, events);
            var progress = DuelAftermath.Advance(world, DuelAftermath.Begin(world, outcome), rules, events);
            if (progress is not null) return (progress, tacticReady.Value);
        }
        if (tacticReady == true) TacticCombat.ResolveOpportunity(world, sourceId, order, rules, events, exchange);
        return null;
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingDuel is not { } pending) return;
        var state = world.Simulation;
        if (!pending.XunStarted.IsXunBoundary || pending.XunStarted.DayNumber < state.Started.DayNumber ||
            world.Clock.DayNumber <= pending.XunStarted.DayNumber || world.Clock.DayNumber > pending.XunStarted.Advance().DayNumber ||
            state.ActiveControl != state.Controls.Length || pending.Aftermath.PendingRulerId is not int ruler ||
            !world.Officers.TryGetValue(ruler, out var officer) || !officer.IsServing || officer.OwnerId != pending.RespondingForceId ||
            world.Forces[pending.RespondingForceId].LeaderId != ruler ||
            !state.Controls.Any(c => c.ForceId == pending.RespondingForceId) ||
            DuelAftermath.Successors(world, ruler).IsEmpty || pending.Exchange.Committed ||
            !CombatExchange.ValidContinuation(world, pending.Exchange) ||
            pending.Exchange.Source.IsSettlement || pending.Exchange.Target.IsSettlement ||
            pending.Aftermath.Outcome.Roster.AttackingUnitId != (pending.Counter ? pending.Exchange.Target.Id : pending.Exchange.Source.Id) ||
            pending.Aftermath.Outcome.Roster.DefendingUnitId != (pending.Counter ? pending.Exchange.Source.Id : pending.Exchange.Target.Id) ||
            pending.Day.RemainingUnits.IsDefault ||
            pending.Day.UsedUnits.IsDefault || pending.Day.UsedBuildings.IsDefault ||
            !pending.Day.RemainingUnits.SequenceEqual(pending.Day.RemainingUnits.Distinct().Order()))
            throw new ArgumentException("Invalid pending duel succession.");
    }
}
