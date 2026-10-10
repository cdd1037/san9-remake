using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Bounded attack, retreat and reinforcement policy using the shared command validator.</summary>
public static class MilitaryPlanner
{
    public static GameCommand Choose(GameSession session, ControlSeat seat, Guid commandId) =>
        Choose(session.Snapshot, session.Validate, session.MilitaryRules, seat, commandId);

    internal static GameCommand Choose(WorldSnapshot world,
        Func<CommandContext, GameCommand, CommandSubmissionResult> validate, MilitaryRulebook? rules,
        ControlSeat seat, Guid commandId, int? corpsId = null)
    {
        world = AiPlanningWorld.Read(world);
        var wait = new WaitCommand(commandId, seat.ForceId, world.Simulation.NextSequence);
        if (rules is null) return wait;
        var force = world.Forces.Single(f => f.Id == seat.ForceId);
        var formations = rules.Formations.Values.Where(f => rules.CanSelectFormation(force, f.Id))
            .OrderBy(f => f.DeploymentCost).ThenBy(f => f.Id).ToArray();
        var work = new NavigationWorkBudget(100000);
        var attack = ChooseAttack(world, validate, rules, seat, commandId, corpsId, formations, work);
        if (attack is not null) return attack;
        if (work.Exhausted) return wait;
        var own = world.Settlements.Where(s => s.OwnerId == seat.ForceId && s.CanHostPersons && s.Durability > 0)
            .OrderBy(s => s.Id).ToArray();
        var incoming = world.Units.Where(u => u.OwnerId == seat.ForceId &&
            u.Order is { SettlementId: not null, Kind: MilitaryOrderKind.Return or MilitaryOrderKind.Transport })
            .GroupBy(u => u.Order!.SettlementId!.Value).ToDictionary(g => g.Key, g => g.Sum(u => (long)u.Soldiers));
        var candidates = from source in own
            where source.Soldiers > 4000 && (corpsId is int id ? source.CorpsId == id :
                source.CorpsId is null || world.Corps.Any(c => c.Id == source.CorpsId && c.ControlMode == CorpsControlMode.Direct))
            from target in own
            let expected = target.Soldiers + incoming.GetValueOrDefault(target.Id)
            where source.Id != target.Id && expected < 3000 && expected < target.SoldierCapacity
            orderby expected, source.Soldiers descending, source.Id, target.Id
            select new { Source = source, Target = target, Expected = expected };
        foreach (var candidate in candidates.Take(8))
        {
            var actor = world.Officers.Where(p => p.IsServing && p.OwnerId == seat.ForceId &&
                p.SettlementId == candidate.Source.Id && !p.Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id))
                .OrderBy(p => p.Status == OfficerStatus.Ruler || p.Status == OfficerStatus.Governor).ThenBy(p => p.Id).FirstOrDefault();
            if (actor is null) continue;
            uint soldiers = (uint)Math.Min(candidate.Source.Soldiers - 3000L,
                Math.Min(3000 - candidate.Expected, candidate.Target.SoldierCapacity - candidate.Expected));
            if (rules.TransportFormations is not { } pair) return wait;
            var formation = rules.Formations[pair.LandFormationId];
            uint capacity = pair.NavalFormationId is int naval
                ? Math.Min(formation.SoldierCapacity, rules.Formations[naval].SoldierCapacity) : formation.SoldierCapacity;
            uint count = Math.Min(soldiers, capacity);
            if (count < 1000) continue;
            var command = new TransportUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence,
                candidate.Source.Id, [actor.Id], count, candidate.Target.Id);
            if (!validate(new(seat.ControllerId), command).Accepted) continue;
            // Static topology is public content. Enemy unit positions are not planning inputs.
            var occupied = MilitaryRouteQuery.Occupancy(world, rules, seat.ForceId, command.Policies, includeUnits: false);
            var travel = rules.Travel(pair.LandFormationId, pair.NavalFormationId);
            var target = rules.Place(candidate.Target);
            bool reachable = MilitaryRouteQuery.DeploymentPosition(travel, rules.SettlementArea(candidate.Source), occupied) is { } start &&
                travel.Map.ReachabilityIntoRange(start, rules.BuildingArea(target), travel.Shape,
                    travel.Mode, occupied, work, 1) == RouteReachability.Reachable;
            if (reachable) return command;
            if (work.Exhausted) return wait;
        }
        return wait;
    }

    private static GameCommand? ChooseAttack(WorldSnapshot world, Func<CommandContext, GameCommand, CommandSubmissionResult> validate,
        MilitaryRulebook rules, ControlSeat seat, Guid commandId, int? corpsId, FormationRule[] formations, NavigationWorkBudget work)
    {
        bool Controlled(int? id) => corpsId is int expected ? id == expected : id is null ||
            world.Corps.Any(c => c.Id == id && c.ControlMode == CorpsControlMode.Direct);
        var delegation = corpsId is int corps ? world.Corps.Single(c => c.Id == corps).Delegation : new GeneralDelegation();
        if (delegation is DomesticDelegation) return null;
        bool MatchesDelegation(SettlementState target) => delegation switch
        {
            DefeatForceDelegation force => target.OwnerId == force.ForceId,
            CaptureSettlementDelegation place => target.Id == place.SettlementId,
            _ => true
        };
        var obligation = world.Simulation.AttackRequests.SingleOrDefault(r => r.RequestedForceId == seat.ForceId &&
            r.RemainingXun > 0 && world.Forces.Any(f => f.Id == r.RequesterForceId && !f.IsEliminated) &&
            world.Settlements.Any(s => s.Id == r.TargetSettlementId && !s.IsDestroyed && s.OwnerId == r.TargetForceId));
        // Public place ownership and topology only: neither enemy strength nor hidden field positions inform this policy.
        var targets = world.Settlements.Where(s => MatchesDelegation(s) && s.OwnerId != seat.ForceId && !s.IsDestroyed &&
            (s.OwnerId is not int other || world.Simulation.Alliance?.Protects(seat.ForceId, other) != true &&
                !world.Simulation.AttackRequests.Any(r => r.Connects(seat.ForceId, other)) &&
                (world.Relations.FirstOrDefault(r => r.FirstId == Math.Min(seat.ForceId, other) && r.SecondId == Math.Max(seat.ForceId, other))?.Value ?? 0) < 40) &&
            rules.TryPlace(s, out var p) && p.Combat is not null).OrderBy(s => s.Id).ToArray();
        if (targets.Length == 0) return null;
        var routes = new Dictionary<(WorldPosition, BattlefieldMap, Footprint, TravelMode, OrdinaryAttack?, OrdinaryAttack?, int, int?), bool>();
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && Controlled(u.CorpsId) && u.Order is null).OrderBy(u => u.Id).Take(8))
        {
            if (unit.FormationId is not int f || rules.Formation(unit).Attack is not { } attack) continue;
            if (unit.Morale < 30 || unit.Soldiers < 500)
            {
                if (unit.ServicePlaceId is int home)
                {
                    var retreat = new OrderUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.Return, null, home));
                    if (validate(new(seat.ControllerId), retreat).Accepted) return retreat;
                }
                continue;
            }
            foreach (var target in PrioritizeTargets(unit.Position))
            {
                if (work.Exhausted) return null;
                var command = new OrderUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.AttackSettlement, null, target.Id));
                if (validate(new(seat.ControllerId), command).Accepted && Reachable(unit.Position, rules.Formations[f], unit.NavalFormationId, target.Id, unit.ServicePlaceId)) return command;
            }
        }
        foreach (var source in world.Settlements.Where(s => s.OwnerId == seat.ForceId && Controlled(s.CorpsId) && s.CanHostPersons &&
            s.Soldiers >= 6000 && s.Morale >= 65 && rules.TryPlace(s, out _)).OrderByDescending(s => s.Soldiers).ThenBy(s => s.Id).Take(4))
        {
            // Preserve a garrison and one administrator; a single officer is sufficient for this bounded policy.
            var people = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && p.SettlementId == source.Id &&
                !p.Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)).OrderBy(p => p.Status == OfficerStatus.Ruler).ThenBy(p => p.Id).ToArray();
            if (people.Length < 2) continue;
            foreach (var target in PrioritizeTargets(rules.Place(source).Position))
            {
                if (world.Units.Any(u => u.OwnerId == seat.ForceId && u.Order is { Kind: MilitaryOrderKind.AttackSettlement } && u.Order.SettlementId == target.Id)) continue;
                foreach (var formation in formations.Where(f => f.Attack is not null).Take(32))
                {
                    if (work.Exhausted) return null;
                    int? navalId = rules.NavalFormationForDeployment(formation.Id);
                    var command = new DeployUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, source.Id,
                        [people[0].Id], Math.Min(source.Soldiers - 3000, formation.SoldierCapacity), formation.Id,
                        new(MilitaryOrderKind.AttackSettlement, null, target.Id)) { NavalFormationId = navalId };
                    var occupied = MilitaryRouteQuery.Occupancy(world, rules, seat.ForceId, command.Policies, includeUnits: false);
                    if (validate(new(seat.ControllerId), command).Accepted &&
                        MilitaryRouteQuery.DeploymentPosition(rules.Travel(formation.Id, navalId), rules.SettlementArea(source), occupied) is { } start &&
                        Reachable(start, formation, navalId, target.Id, source.Id)) return command;
                }
            }
        }
        return null;

        IEnumerable<SettlementState> PrioritizeTargets(WorldPosition from) => targets
            .OrderBy(s => s.Id == obligation?.TargetSettlementId ? 0 : 1)
            .ThenBy(s => MapCoordinates.Distance(from, rules.Place(s).Position)).ThenBy(s => s.Id).Take(4);

        bool Reachable(WorldPosition from, FormationRule formation, int? navalId, int target, int? source)
        {
            var travel = rules.Travel(formation.Id, navalId);
            var navalAttack = navalId is int ship ? rules.Formations[ship].Attack : null;
            var key = (from, travel.Map, travel.Shape, travel.Mode, formation.Attack, navalAttack, target, source);
            OrdinaryAttack? Attack(WorldPosition p) => rules.Formation(formation.Id, navalId, p).Attack;
            if (routes.TryGetValue(key, out bool reachable)) return reachable;
            var settlement = world.Settlements.Single(s => s.Id == target);
            var place = rules.Place(settlement);
            if (!rules.CanAttackTarget(formation.Attack, place.Position, true, rules.IsHarbor(settlement)) &&
                (navalId is not int naval || !rules.CanAttackTarget(rules.Formations[naval].Attack, place.Position, true, rules.IsHarbor(settlement)))) return false;
            int MaximumRange(OrdinaryAttack? attack) => attack is null ? 0 : checked(attack.Range + attack.MaximumElevationBonus);
            int maximumRange = Math.Max(MaximumRange(formation.Attack), MaximumRange(navalAttack));
            var area = rules.BuildingArea(place);
            var result = travel.Map.ReachabilityIntoRange(from, area, travel.Shape,
                travel.Mode, MilitaryRouteQuery.Occupancy(world, rules, seat.ForceId, new(), includeUnits: false)
                    .Where(o => o.Position != area.Position || o.Footprint != area.Footprint),
                work, maximumRange, position => rules.AttackRange(Attack(position), position, place.Position, true, rules.IsHarbor(settlement)));
            if (result == RouteReachability.Incomplete) return false;
            reachable = result == RouteReachability.Reachable;
            routes.Add(key, reachable);
            return reachable;
        }
    }
}
