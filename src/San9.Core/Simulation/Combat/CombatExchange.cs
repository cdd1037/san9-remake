using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Unscaled ordinary damage belongs to one exchange, including across a pending decision.</summary>
public sealed record CombatExchangeState(BattleSource Source, BattleSource Target, int? SourceOwnerId, int? TargetOwnerId,
    uint DamageToTarget, uint DamageToSource, bool CounterAllowed, bool Committed);

internal sealed class CombatExchange(CombatExchangeState state)
{
    internal CombatExchangeState State { get; private set; } = state;
    internal bool Committed => State.Committed;
    internal BattleSource Source => State.Source;
    internal BattleSource Target => State.Target;

    internal static MilitaryOrder Attack(BattleSource target) => target.IsSettlement
        ? new(MilitaryOrderKind.AttackSettlement, null, target.Id)
        : new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id };

    internal static BattleSource TargetOf(MilitaryOrder order) =>
        new(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null);

    private static int? Owner(WorldMutation world, BattleSource actor) => actor.IsSettlement
        ? world.Settlements[actor.Id].OwnerId : world.Units[actor.Id].OwnerId;

    internal static bool ValidContinuation(WorldMutation world, CombatExchangeState state) =>
        state.Source != state.Target && !(state.Source.IsSettlement && state.Target.IsSettlement) &&
        state.Source.Exists(world) && state.Target.Exists(world) &&
        Owner(world, state.Source) == state.SourceOwnerId && Owner(world, state.Target) == state.TargetOwnerId &&
        (state.CounterAllowed || state.DamageToSource == 0);

    private static bool Present(WorldMutation world, BattleSource actor) => actor.IsSettlement
        ? world.Settlements.TryGetValue(actor.Id, out var place) && !MilitaryCombat.Defeated(place) && !place.IsDestroyed
        : world.Units.TryGetValue(actor.Id, out var unit) && unit.Soldiers > 0 && unit.Morale > 0;

    internal bool CanContinue(WorldMutation world) => Present(world, Source) && Present(world, Target) &&
        Owner(world, Source) == State.SourceOwnerId && Owner(world, Target) == State.TargetOwnerId;

    internal bool CanOfferTactics(WorldMutation world)
    {
        bool Survives(BattleSource source, BattleSource target, uint damage) => target.IsSettlement &&
            !world.Settlements[target.Id].CanHostPersons ||
            MilitaryCombat.FinalDamage(world, source, Owner(world, target), damage, target.Soldiers(world)) < target.Soldiers(world);
        return CanContinue(world) && Survives(Source, Target, State.DamageToTarget) && Survives(Target, Source, State.DamageToSource);
    }

    internal static bool CanRetaliate(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook rules) => target.IsSettlement
        ? FacilityCombat.Active(world.Settlements[target.Id]) && rules.FacilityAttack(world.Settlements[target.Id]).Attack is not null &&
            target.Area(world, rules).IntersectsExpanded(source.Area(world, rules),
                MilitaryCombat.FacilityRange(world, world.Settlements[target.Id], world.Units[source.Id], rules))
        : world.Units[target.Id] is var unit && unit.Condition is not (UnitCondition.Confused or UnitCondition.Routed) &&
            rules.Formation(unit).Attack is not null && target.Area(world, rules).IntersectsExpanded(source.Area(world, rules),
                MilitaryCombat.UnitRange(world, unit, Attack(source), rules));

    internal static CombatExchange Calculate(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook rules)
    {
        bool counter = CanRetaliate(world, source, target, rules);
        return new(new(source, target, Owner(world, source), Owner(world, target),
            MilitaryCombat.NormalDamage(world, source, target, rules),
            counter ? MilitaryCombat.NormalDamage(world, target, source, rules) : 0, counter, false));
    }

    internal uint OrdinaryDamage(BattleSource source) => source == Source ? State.DamageToTarget :
        source == Target ? State.DamageToSource : throw new ArgumentException("Actor does not belong to this exchange.", nameof(source));

    internal void CommitNormal(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (Committed) return;
        if (!CanContinue(world)) { State = State with { Committed = true }; return; }
        Commit(world, rules.Military!, events, null, 0, null, null);
        if (ResolveDefeats(world, rules, events) is int place)
            BattleAftermath.ResolveSettlementDefeat(world, place, Source.IsSettlement ? Target : Source, rules, events);
    }

    internal void CommitMartial(WorldMutation world, MilitaryRulebook rules, List<DomainEvent> events,
        BattleSource source, uint damage, int tacticId, uint calculatedDamage)
    {
        if (Committed) throw new InvalidOperationException("Exchange damage has already been committed.");
        Commit(world, rules, events, source, damage, tacticId, calculatedDamage);
    }

    private void Commit(WorldMutation world, MilitaryRulebook rules, List<DomainEvent> events,
        BattleSource? martialSource, uint martialDamage, int? tacticId, uint? calculatedDamage)
    {
        State = State with { Committed = true };
        Apply(Source, Target, martialSource == Source ? martialDamage : State.DamageToTarget);
        Apply(Target, Source, martialSource == Target ? martialDamage : State.DamageToSource);

        void Apply(BattleSource source, BattleSource target, uint damage)
        {
            bool martial = martialSource == source;
            if (target.IsSettlement)
                MilitaryCombat.DamageSettlement(world, world.Settlements[target.Id], damage,
                    rules.Place(world.Settlements[target.Id]).Combat!, source.Id, events, attackerIsSettlement: source.IsSettlement,
                    tacticId: martial ? tacticId : null, calculatedSoldierLoss: martial ? calculatedDamage : null);
            else
                MilitaryCombat.DamageUnit(world, world.Units[target.Id], damage, rules.CombatFormation(world.Units[target.Id])!,
                    source.Id, events, source.IsSettlement, martial ? tacticId : null, martial ? calculatedDamage : null);
        }
    }

    // A captured settlement is returned so martial growth finishes before its source is absorbed.
    internal int? ResolveDefeats(WorldMutation world, SimulationRules rules, List<DomainEvent> events,
        BattleSource? martialSource = null, UnitDefeatCause cause = UnitDefeatCause.Other)
    {
        bool Lost(BattleSource actor) => actor.IsSettlement ? MilitaryCombat.Defeated(world.Settlements[actor.Id]) :
            !world.Units.TryGetValue(actor.Id, out var unit) || unit.Soldiers == 0 || unit.Morale == 0;
        bool sourceLost = Lost(Source), targetLost = Lost(Target);
        Defeat(Target, targetLost, Source, sourceLost);
        Defeat(Source, sourceLost, Target, targetLost);
        return Source.IsSettlement && sourceLost && !targetLost ? Source.Id :
            Target.IsSettlement && targetLost && !sourceLost ? Target.Id : null;

        void Defeat(BattleSource actor, bool lost, BattleSource opponent, bool opponentLost)
        {
            if (!lost || actor.IsSettlement || !world.Units.ContainsKey(actor.Id)) return;
            BattleSource? winner = opponentLost || opponent.IsSettlement && !world.Settlements[opponent.Id].CanHostPersons ? null : opponent;
            BattleAftermath.TerminateUnit(world, actor.Id, winner, rules, events,
                martialSource == opponent ? cause : UnitDefeatCause.Other);
        }
    }
}
