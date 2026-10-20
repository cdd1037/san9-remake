using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record GuanduEvent(GameClock Clock, int ForceId, int OpposingForceId, int RecruitingOfficerId,
    int RecruitedOfficerId, ImmutableArray<int> AffectedUnitIds, ImmutableArray<int> AffectedSettlementIds,
    long SoldiersLost, uint FoodLost, int BaseTrustGain, int EnemyBaseTrustLoss) : DomainEvent;

internal static class Guandu
{
    internal static bool Eligible(WorldMutation world, int actorId, int targetId, SimulationRules rules)
    {
        if (rules.Guandu is not { } d || rules.Military is not { } military || targetId != d.RecruitId ||
            world.Simulation.ConsumedEventKeys.Contains(d.ConsumptionKey) ||
            !world.Officers.TryGetValue(d.PatronId, out var patron) || !patron.IsServing ||
            !world.Officers.TryGetValue(d.OpponentId, out var opponent) || !HasAffiliation(opponent) ||
            !world.Officers.TryGetValue(actorId, out var actor) || !actor.IsServing ||
            !world.Officers.TryGetValue(targetId, out var recruit) || !HasAffiliation(recruit) || recruit.Status == OfficerStatus.Ruler ||
            recruit.IsCommander || patron.OwnerId is not int owner || opponent.OwnerId is not int rival || owner == rival ||
            actor.OwnerId != owner || recruit.OwnerId != rival || recruit.Loyalty >= d.LoyaltyExclusive ||
            !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated ||
            !world.Forces.TryGetValue(rival, out var enemy) || enemy.IsEliminated || ResourceAccounts.Root(world, enemy.Id).Treasury.Food == 0 ||
            !world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) ||
            ForceRelations.Get(world, owner, rival).Value >= d.RelationExclusive ||
            world.Simulation.Alliance?.Protects(owner, rival) == true ||
            !world.Settlements.TryGetValue(d.RequiredPlaceId, out var required) || required.IsDestroyed || required.OwnerId != owner ||
            world.IsTraveling(patron.Id)) return false;

        int? patronRegion = patron.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit)
            ? !unit.IsPersonnel ? unit.IsEmbedded ? unit.ServicePlaceId : military.RegionAt(unit.Position) : null
            : patron.SettlementId is int home && world.Settlements.TryGetValue(home, out var place) &&
                IsGarrison(place) && place.OwnerId == owner ? home : null;
        return patronRegion is int region && d.PatronRegionIds.Contains(region) &&
            Targets(world, d, military, rival).Sum(u => (long)u.Soldiers) +
            Garrisons(world, d, rival).Sum(s => (long)s.Soldiers) >= d.MinimumEnemySoldiers;
    }

    private static bool HasAffiliation(OfficerState person) => person.IsServing || person.Status == OfficerStatus.Captive;
    private static bool IsGarrison(SettlementState s) => !s.IsDestroyed && s.CanHostPersons && s.Facility?.BuilderUnitId is null;
    private static IEnumerable<SettlementState> Garrisons(WorldMutation world, GuanduDefinition d, int rival) =>
        world.Settlements.Values.Where(s => s.OwnerId == rival && IsGarrison(s) && d.EnemyRegionIds.Contains(s.Id));
    private static IEnumerable<UnitState> Targets(WorldMutation world, GuanduDefinition d, MilitaryRulebook military, int rival) =>
        world.Units.Values.Where(u => u.OwnerId == rival && !u.IsEmbedded && !u.IsPersonnel &&
            military.RegionAt(u.Position) is int region && d.EnemyRegionIds.Contains(region));

    internal static bool Apply(WorldMutation world, int actorId, int targetId, SimulationRules rules, List<DomainEvent> events)
    {
        if (!Eligible(world, actorId, targetId, rules)) return false;
        var d = rules.Guandu!;
        var military = rules.Military!;
        int owner = world.Officers[actorId].OwnerId!.Value, rival = world.Officers[d.OpponentId].OwnerId!.Value;
        var targets = Targets(world, d, military, rival).OrderBy(u => u.Id).ToArray();
        var garrisons = Garrisons(world, d, rival).OrderBy(s => s.Id).ToArray();
        long lost = 0;
        bool terminated = false;
        foreach (var place in garrisons)
        {
            uint loss = (uint)((long)place.Soldiers * d.SoldierLossPercent / 100);
            world.Replace(place with { Soldiers = place.Soldiers - loss });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, place.Id), ResourceKind.Soldiers,
                "guandu", actorId, -(long)loss, -(long)loss, events);
            lost += loss;
        }
        foreach (var unit in targets)
        {
            uint loss = (uint)((long)unit.Soldiers * d.SoldierLossPercent / 100);
            world.Replace(unit with { Soldiers = unit.Soldiers - loss });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers,
                "guandu", actorId, -(long)loss, -(long)loss, events);
            lost += loss;
            if (unit.Soldiers == loss)
            {
                BattleAftermath.TerminateUnit(world, unit.Id, null, rules, events);
                terminated = true;
                continue;
            }
            if (unit.EngagementTargetId is not null)
                MilitaryPolicies.Engage(world, world.Units[unit.Id], null, events, "guandu");
            if (unit.EffectiveOrderClass == UnitOrderClass.Military && unit.FormationId is not null)
                MilitaryPolicies.Return(world, world.Units[unit.Id], military, events, "guandu");
        }
        if (terminated)
        {
            GovernanceLifecycle.Reconcile(world, new HashSet<int> { rival }, rules, events, true, "guandu");
            world.MaintainLeadership(events);
            ResolutionPipeline.CheckConquest(world, events);
        }
        uint foodLoss = (uint)((long)ResourceAccounts.Root(world, rival).Treasury.Food * d.FoodLossPercent / 100);
        ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, rival, null), ResourceKind.Food, -(long)foodLoss, "guandu", actorId, events);
        var force = world.Forces[owner]; var enemy = world.Forces[rival];
        int gain = (int)Math.Min(d.BaseTrustBonus, 1000L - force.BaseTrust), penalty = Math.Min(d.EnemyBaseTrustPenalty, enemy.BaseTrust);
        world.Replace(force with { BaseTrust = force.BaseTrust + gain });
        world.Replace(enemy with { BaseTrust = enemy.BaseTrust - penalty });
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing && p.Status != OfficerStatus.Ruler).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, owner, (int)Math.Min(255L, person.LoyaltyTo(owner) + (long)d.LoyaltyBonus), "guandu", events);
        var candidate = new EventCandidate(d.ConsumptionKey.Value, EventTrigger.Recruitment, 0, EventConsumption.OnSuccess, d.ConsumptionKey);
        world.ConsumeEvent(candidate, new(candidate.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new GuanduEvent(world.Clock, owner, rival, actorId, targetId, targets.Select(u => u.Id).ToImmutableArray(),
            garrisons.Select(s => s.Id).ToImmutableArray(), lost, foodLoss, gain, penalty));
        return true;
    }
}
