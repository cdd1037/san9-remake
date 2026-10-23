using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RedCliffsEvent(GameClock Clock, int ForceId, int OpposingForceId,
    ImmutableArray<int> AffectedUnitIds, ImmutableArray<int> AffectedSettlementIds, long SoldiersLost,
    int BaseTrustGain, int EnemyBaseTrustLoss) : DomainEvent;

internal static class RedCliffs
{
    internal static bool Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.NewGame.Mode != GameMode.Historical || rules.RedCliffs is not { } definition || rules.Military is not { } military ||
            world.Simulation.ConsumedEventKeys.Contains(definition.ConsumptionKey)) return false;
        if (!world.Officers.TryGetValue(definition.OfficerId, out var person)) return false;
        if (person.OwnerId is not int owner || !world.Forces.TryGetValue(owner, out var force) || force.IsEliminated ||
            !world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) ||
            !world.Settlements.TryGetValue(definition.OpposingCityId, out var city) || city.IsDestroyed ||
            city.OwnerId is not int rival || rival == owner || !world.Forces.TryGetValue(rival, out var enemy) || enemy.IsEliminated ||
            enemy.LeaderId is not int leader || definition.ExcludedRulerIds.Contains(leader) ||
            ForceRelations.Get(world, owner, rival).Value >= definition.RequiredRelationExclusive ||
            world.IsTraveling(person.Id)) return false;
        int? location = person.UnitId is int unitId ? military.RegionAt(world.Units[unitId].Position) : person.SettlementId ?? person.RegionId;
        if (location != definition.OfficerRegionId) return false;

        var regions = definition.RegionIds.ToHashSet();
        var fleets = world.Units.Values.Where(u => (u.OwnerId == owner || u.OwnerId == rival) &&
            !u.IsEmbedded && !u.IsPersonnel && military.Map.IsWater(u.Position) &&
            military.RegionAt(u.Position) is int region && regions.Contains(region)).OrderBy(u => u.Id).ToArray();
        if (fleets.Count(u => u.OwnerId == owner) < definition.MinimumFriendlyWaterUnits) return false;
        var targets = fleets.Where(u => u.OwnerId == rival).ToArray();
        var garrisons = world.Settlements.Values.Where(s => s.OwnerId == rival && !s.IsDestroyed && s.CanHostPersons &&
            s.Facility?.BuilderUnitId is null && regions.Contains(s.Id)).OrderBy(s => s.Id).ToArray();
        if (targets.Sum(u => (long)u.Soldiers) + garrisons.Sum(s => (long)s.Soldiers) < definition.MinimumEnemySoldiers) return false;

        long lost = 0;
        bool terminated = false;
        foreach (var target in garrisons)
        {
            uint loss = (uint)((long)target.Soldiers * definition.SoldierLossPercent / 100);
            world.Replace(target with { Soldiers = target.Soldiers - loss });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, target.Id), ResourceKind.Soldiers,
                "red-cliffs", person.Id, -(long)loss, -(long)loss, events);
            lost += loss;
        }
        foreach (var target in targets)
        {
            uint loss = (uint)((long)target.Soldiers * definition.SoldierLossPercent / 100);
            world.Replace(target with { Soldiers = target.Soldiers - loss });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, target.Id), ResourceKind.Soldiers,
                "red-cliffs", person.Id, -(long)loss, -(long)loss, events);
            lost += loss;
            if (target.Soldiers == loss)
            {
                BattleAftermath.TerminateUnit(world, target.Id, null, rules, events);
                terminated = true;
            }
            else
            {
                if (target.EngagementTargetId is not null)
                    MilitaryPolicies.Engage(world, world.Units[target.Id], null, events, "red-cliffs");
                if (target.Order is not null && target.FormationId is not null)
                    MilitaryOrders.Return(world, world.Units[target.Id], military, events, "red-cliffs");
            }
        }
        if (terminated)
        {
            GovernanceLifecycle.Reconcile(world, new HashSet<int> { rival }, rules, events, false, "red-cliffs");
            world.MaintainLeadership(events);
            ResolutionPipeline.CheckConquest(world, events);
        }
        int gain = (int)Math.Min(definition.BaseTrustBonus, 1000L - force.BaseTrust);
        int penalty = Math.Min(definition.EnemyBaseTrustPenalty, enemy.BaseTrust);
        world.Replace(world.Forces[owner] with { BaseTrust = force.BaseTrust + gain });
        world.Replace(world.Forces[rival] with { BaseTrust = enemy.BaseTrust - penalty });
        foreach (var officer in world.Officers.Values.Where(p => p.OwnerId == owner && p.IsServing && p.Status != OfficerStatus.Ruler).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, officer.Id, owner, (int)Math.Min(255L, (long)officer.LoyaltyTo(owner) + definition.LoyaltyBonus), "red-cliffs", events);
        var candidate = new EventCandidate(definition.ConsumptionKey.Value, EventTrigger.Xun, 0, EventConsumption.OnSuccess, definition.ConsumptionKey);
        world.ConsumeEvent(candidate, new(candidate.Id, EventOutcomeKind.Completed, false, EventResult.Succeeded));
        events.Add(new RedCliffsEvent(world.Clock, owner, rival, targets.Select(u => u.Id).ToImmutableArray(),
            garrisons.Select(s => s.Id).ToImmutableArray(), lost, gain, penalty));
        return true;
    }
}
