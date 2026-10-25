using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ChooseSnipingSuccessorCommand(Guid CommandId, int IssuerId, long Sequence, int RulerId,
    [property: JsonRequired] int? SuccessorId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SnipingSuccessionPrompt(int RulerId, int RespondingForceId, ImmutableArray<int> CandidateIds);
public sealed record SnipingSuccessionRequestedEvent(GameClock Clock, int RulerId, int RespondingForceId) : DomainEvent;

// Member snapshots preserve the original growth eligibility across death and succession.
public sealed record TacticAftermathContinuation(BattleSource Source, MilitaryOrder Target, int TacticId,
    int? LearningTacticId, ImmutableArray<OfficerState> Members, ImmutableArray<int> Participants);
public sealed record CombatExchangeContinuation(GameClock XunStarted, CombatDayContinuation Day,
    BattleSource Source, BattleSource Target, ImmutableArray<int> AffectedForces,
    int? PreviousSettlementOwnerId = null);
public sealed record SnipingCombatDecision(int RulerId, int RespondingForceId,
    TacticAftermathContinuation? Tactic = null, CombatExchangeContinuation? Exchange = null);

internal static class SnipingSuccession
{
    internal static void CaptureTactic(WorldMutation world, TacticAftermathContinuation tactic) =>
        world.SetSimulation(world.Simulation with { PendingSniping = world.Simulation.PendingSniping! with { Tactic = tactic } });

    internal static bool CaptureExchange(WorldMutation world, CombatExchangeContinuation exchange)
    {
        if (world.Simulation.PendingSniping is not { } pending) return false;
        world.SetSimulation(world.Simulation with { PendingSniping = pending with { Exchange = exchange } });
        return true;
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingSniping is not { } pending) return;
        static bool Ordered(ImmutableArray<int> ids) => !ids.IsDefault && ids.All(id => id >= 0) &&
            ids.SequenceEqual(ids.Distinct().Order());
        static bool Targets(TacticAftermathContinuation tactic, BattleSource target) => target.IsSettlement
            ? tactic.Target.UnitId is null && tactic.Target.SettlementId == target.Id
            : tactic.Target.SettlementId is null && tactic.Target.UnitId == target.Id;
        if (pending.Exchange is not { } exchange || pending.Tactic is not { } tactic ||
            !CapturedRulerDisposition.ValidContinuation(world, exchange.XunStarted) ||
            !world.Officers.TryGetValue(pending.RulerId, out var ruler) || !ruler.IsServing || ruler.Injury <= 0 ||
            ruler.OwnerId != pending.RespondingForceId || world.Forces[pending.RespondingForceId].LeaderId != ruler.Id ||
            !world.Simulation.Controls.Any(c => c.ForceId == pending.RespondingForceId) ||
            !ForceLifecycle.HasCity(world, pending.RespondingForceId) || RulerSuccession.Candidates(world, ruler.Id).IsEmpty ||
            !Ordered(exchange.Day.RemainingUnits) || !Ordered(exchange.Day.UsedUnits) || !Ordered(exchange.Day.UsedBuildings) ||
            exchange.Day.RemainingBuildings is { } buildings && !Ordered(buildings) ||
            !Ordered(exchange.AffectedForces) || exchange.AffectedForces.IsEmpty ||
            exchange.AffectedForces.Any(id => !world.Forces.ContainsKey(id)) ||
            exchange.Source.IsSettlement && exchange.Target.IsSettlement ||
            !exchange.Source.IsCombatant(world) || !exchange.Target.IsCombatant(world) ||
            tactic.Source != exchange.Source && tactic.Source != exchange.Target ||
            tactic.TacticId < 0 || tactic.LearningTacticId is < 0 || tactic.Members.IsDefaultOrEmpty ||
            tactic.Members.Select(p => p.Id).Distinct().Count() != tactic.Members.Length ||
            tactic.Participants.IsDefaultOrEmpty || tactic.Participants.Distinct().Count() != tactic.Participants.Length ||
            tactic.Participants.Any(id => !tactic.Members.Any(p => p.Id == id)) ||
            !Targets(tactic, tactic.Source == exchange.Source ? exchange.Target : exchange.Source))
            throw new ArgumentException("Invalid pending sniping succession.");
    }
}
