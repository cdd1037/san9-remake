using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record IntelligenceEffectResolvedEvent(GameClock Clock, BattleSource Source, int TargetId, bool TargetIsSettlement,
    int TacticId, int Effect) : DomainEvent;
public sealed record TacticMoraleChangedEvent(GameClock Clock, int TargetId, bool TargetIsSettlement, int Previous, int Current,
    int TacticId) : DomainEvent;

internal static class IntelligenceTactics
{
    internal static bool IsOffensive(TacticEffect effect) => effect is TacticEffect.Confusion or TacticEffect.Defection or
        TacticEffect.Illusion or TacticEffect.Taunt or TacticEffect.Sorcery or TacticEffect.Trap;

    internal static OfficerState[] Defenders(WorldMutation world, MilitaryOrder target) => world.Officers.Values
        .Where(p => p.IsServing && (target.UnitId is int unit ? p.UnitId == unit : p.SettlementId == target.SettlementId &&
            !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null))).OrderBy(p => p.Id).ToArray();

    internal static int Effect(WorldMutation world, OfficerState actor, TacticRule tactic, MilitaryOrder target, SimulationRules rules)
    {
        var military = rules.Military!;
        var defenders = Defenders(world, target);
        var formation = target.UnitId is int id ? military.Formations[world.Units[id].FormationId!.Value].Tactics : null;
        bool confused = tactic.Effect == TacticEffect.Confusion;
        if (confused && target.UnitId is int enemy && world.Units[enemy].Condition == UnitCondition.HighSpirits) return 0;
        TacticEffect? counter = confused ? TacticEffect.CounterConfusion : tactic.Effect == TacticEffect.Trap ? TacticEffect.CounterTrap : null;
        int? owner = target.UnitId is int unit ? world.Units[unit].OwnerId : world.Settlements[target.SettlementId!.Value].OwnerId;
        if (counter is not null && formation?.BypassesPassiveCounters != true && owner is int force &&
            defenders.Any(p => p.EquippedTacticId is int equipped && military.Tactics[equipped].Effect == counter &&
                MilitaryTactics.HasTactic(world, force, [p.Id], equipped, rules.Treasures))) return 0;
        int chance = TacticMath.IntelligenceSuccessChance(world.Abilities(actor).Intelligence,
            defenders.Select(p => world.Abilities(p).Intelligence).DefaultIfEmpty(0).Max());
        if (!TacticCombat.Roll(world, "military-tactic-intelligence", chance)) return 0;
        if (confused && TacticCombat.Roll(world, "military-tactic-formation-resistance", formation?.ConfusionResistanceChance ?? 0)) return 0;
        int category = tactic.Category!.Value;
        int proficiency = defenders.Select(p => p.TacticProficiencies[category]).DefaultIfEmpty(0).Max();
        return TacticCombat.Roll(world, "military-tactic-intelligence-reduction",
            Math.Max(0, (proficiency - actor.TacticProficiencies[category]) / 10)) ? 3 : 10;
    }

    internal static void Resolve(WorldMutation world, BattleSource source, OfficerState actor, TacticRule tactic, MilitaryOrder primary,
        int primaryEffect, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        var area = MilitaryCombat.TargetArea(world, primary, military);
        List<MilitaryOrder> targets = [primary];
        if (tactic.EffectRadius > 0 && tactic.Effect is TacticEffect.Illusion or TacticEffect.Sorcery)
        {
            targets.AddRange(world.Units.Values.Where(u => u.Id != primary.UnitId && MilitaryCombat.Hostile(world, source.Owner(world), u.OwnerId) &&
                u.FormationId is int f && military.Formations[f].Combat is not null && MilitaryCombat.Area(u, military).DistanceTo(area) <= tactic.EffectRadius)
                .OrderBy(u => u.Id).Select(u => new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = u.Id }));
            if (tactic.AffectsFacilities)
                targets.AddRange(world.Settlements.Values.Where(s => s.Id != primary.SettlementId && !s.IsDestroyed && s.Facility?.BuilderUnitId is null &&
                    MilitaryCombat.Hostile(world, source.Owner(world), s.OwnerId) && military.TryPlace(s, out var p) && p.Combat is not null &&
                    new MapOccupancy(p.Position, p.Footprint).DistanceTo(area) <= tactic.EffectRadius).OrderBy(s => s.Id)
                    .Select(s => new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, s.Id)));
        }
        HashSet<int> forces = [source.Owner(world)];
        foreach (var target in targets)
        {
            int? owner = target.UnitId is int enemy ? world.Units[enemy].OwnerId : world.Settlements[target.SettlementId!.Value].OwnerId;
            if (owner is int force) forces.Add(force);
            int effect = target == primary ? primaryEffect : Effect(world, actor, tactic, target, rules);
            events.Add(new IntelligenceEffectResolvedEvent(world.Clock, source, target.UnitId ?? target.SettlementId!.Value,
                target.UnitId is null, tactic.Id, effect));
            if (effect == 0) continue;
            Apply(world, source, actor, tactic, target, effect, rules, events);
        }
        BattleAftermath.Reconcile(world, forces, rules, events);
        ResolutionPipeline.CheckConquest(world, events);
    }

    private static void Apply(WorldMutation world, BattleSource source, OfficerState actor, TacticRule tactic, MilitaryOrder target,
        int effect, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        int proficiency = actor.TacticProficiencies[tactic.Category!.Value];
        int intelligence = world.Abilities(actor).Intelligence;
        uint soldiers = target.UnitId is int enemy ? world.Units[enemy].Soldiers : world.Settlements[target.SettlementId!.Value].Soldiers;
        int morale = target.UnitId is int unit ? world.Units[unit].Morale : world.Settlements[target.SettlementId!.Value].Morale;
        switch (tactic.Effect)
        {
            case TacticEffect.Confusion:
                MilitaryConditions.Confuse(world, target, TacticMath.ConfusionDays(proficiency, intelligence, effect), events);
                break;
            case TacticEffect.Taunt:
                int current = morale - TacticMath.TauntMoraleLoss(proficiency, intelligence, tactic.Power, effect, morale);
                if (target.UnitId is int taunted) world.Replace(world.Units[taunted] with { Morale = current });
                else world.Replace(world.Settlements[target.SettlementId!.Value] with { Morale = current });
                events.Add(new TacticMoraleChangedEvent(world.Clock, target.UnitId ?? target.SettlementId!.Value,
                    target.UnitId is null, morale, current, tactic.Id));
                break;
            case TacticEffect.Defection:
                uint loss = TacticMath.Defection(proficiency, intelligence, tactic.Power, effect, soldiers, target.UnitId is null);
                uint accepted = Math.Min(loss, source.Capacity(world, military) - source.Soldiers(world));
                int? cityId = target.UnitId is int targetUnit
                    ? military.RegionAt(world.Units[targetUnit].Position) is int region
                        ? Demobilization.City(world, region, rules.Personnel) : null
                    : Demobilization.City(world, target.SettlementId!.Value, rules.Personnel);
                if (cityId is null && target.UnitId is int displaced && world.Units[displaced].ServicePlaceId is int home)
                    cityId = Demobilization.City(world, home, rules.Personnel);
                if (cityId is int city)
                    Demobilization.Return(world, city, loss - accepted, "tactic-defection-demobilized",
                        target.UnitId ?? target.SettlementId!.Value, events);
                else loss = accepted;
                source.AddSoldiers(world, accepted);
                if (target.UnitId is int defected) world.Replace(world.Units[defected] with { Soldiers = soldiers - loss });
                else world.Replace(world.Settlements[target.SettlementId!.Value] with { Soldiers = soldiers - loss });
                ResourceAccounts.Report(world, source.Account, ResourceKind.Soldiers, "tactic-defection",
                    target.UnitId ?? target.SettlementId, accepted, accepted, events);
                ResourceAccounts.Report(world, new(target.UnitId is null ? ResourceOwnerKind.Settlement : ResourceOwnerKind.Unit,
                    target.UnitId ?? target.SettlementId!.Value), ResourceKind.Soldiers, "tactic-defection", source.Id, -(long)loss, -(long)loss, events);
                break;
            case TacticEffect.Illusion:
            case TacticEffect.Sorcery:
            case TacticEffect.Trap:
                uint damage = tactic.Effect == TacticEffect.Trap ? TacticMath.TrapDamage(proficiency, intelligence, tactic.Power, effect, soldiers) :
                    TacticMath.IllusionDamage(proficiency, intelligence, tactic.Power, effect, soldiers, tactic.Effect == TacticEffect.Sorcery);
                if (target.UnitId is int damaged)
                    MilitaryCombat.DamageUnit(world, world.Units[damaged], damage, military.CombatFormation(world.Units[damaged])!, source.Id, events, source.IsSettlement);
                else MilitaryCombat.DamageSettlement(world, world.Settlements[target.SettlementId!.Value], damage,
                    military.Place(world.Settlements[target.SettlementId.Value]).Combat!, source.Id, events, attackerIsSettlement: source.IsSettlement);
                break;
            default:
                throw new InvalidOperationException("Not an offensive intelligence effect.");
        }
        if (target.UnitId is int id && world.Units[id] is var victim && (victim.Soldiers == 0 || victim.Morale == 0))
            BattleAftermath.TerminateUnit(world, id, source, rules, events);
        else if (target.UnitId is null && world.Settlements[target.SettlementId!.Value] is var place &&
            MilitaryCombat.Defeated(place))
            BattleAftermath.ResolveSettlementDefeat(world, place.Id, source, rules, events);
    }
}
