using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Bounded attack, retreat and reinforcement policy using the shared command validator.</summary>
public static class MilitaryPlanner
{
    public static GameCommand Choose(GameSession session, ControlSeat seat, Guid commandId) =>
        Choose(session.Snapshot, session.Validate, session.MilitaryRules, seat, commandId);

    internal static GameCommand Choose(WorldSnapshot world,
        Func<CommandContext, GameCommand, CommandSubmissionResult> validate, MilitaryRulebook? rules,
        ControlSeat seat, Guid commandId, int? corpsId = null)
    {
        var wait = new WaitCommand(commandId, seat.ForceId, world.Simulation.NextSequence);
        if (rules is null) return wait;
        var attack = ChooseAttack(world, validate, rules, seat, commandId, corpsId);
        if (attack is not null) return attack;
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
            var routes = new Dictionary<(Footprint, TravelMode), bool>();
            foreach (var formation in rules.Formations.Values.OrderBy(f => f.DeploymentCost).ThenBy(f => f.Id).Take(32))
            {
                uint count = Math.Min(soldiers, formation.SoldierCapacity);
                if (count < 1000) continue;
                var command = new DeployUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence,
                    candidate.Source.Id, [actor.Id], count, formation.Id,
                    new(MilitaryOrderKind.Transport, null, candidate.Target.Id));
                if (!validate(new(seat.ControllerId), command).Accepted) continue;
                // Static topology is public content. Enemy unit positions are not planning inputs.
                var key = (formation.Footprint, formation.TravelMode);
                if (!routes.TryGetValue(key, out bool reachable))
                {
                    var occupied = rules.Places.Values.Where(p => p.Id != candidate.Source.Id && p.Id != candidate.Target.Id)
                        .Select(p => new MapOccupancy(p.Position, p.Footprint));
                    reachable = rules.Map.FindRoute(rules.Places[candidate.Source.Id].Position, rules.Places[candidate.Target.Id].Position,
                        formation.Footprint, formation.TravelMode, occupied).Reachable;
                    routes.Add(key, reachable);
                }
                if (reachable) return command;
            }
        }
        return wait;
    }

    private static GameCommand? ChooseAttack(WorldSnapshot world, Func<CommandContext, GameCommand, CommandSubmissionResult> validate,
        MilitaryRulebook rules, ControlSeat seat, Guid commandId, int? corpsId)
    {
        bool Controlled(int? id) => corpsId is int expected ? id == expected : id is null ||
            world.Corps.Any(c => c.Id == id && c.ControlMode == CorpsControlMode.Direct);
        // Public place ownership and topology only: neither enemy strength nor hidden field positions inform this policy.
        var targets = world.Settlements.Where(s => s.OwnerId != seat.ForceId && !s.IsDestroyed &&
            rules.Places.TryGetValue(s.Id, out var p) && p.Combat is not null).OrderBy(s => s.Id).ToArray();
        if (targets.Length == 0) return null;
        var routes = new Dictionary<(WorldPosition, Footprint, TravelMode, int, int, int?), bool>();
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && Controlled(u.CorpsId) && u.Order is null).OrderBy(u => u.Id).Take(8))
        {
            if (unit.FormationId is not int f || rules.Formations[f].Attack is not { } attack) continue;
            if (unit.Morale < 30 || unit.Soldiers < 500)
            {
                if (unit.ServicePlaceId is int home)
                {
                    var retreat = new OrderUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.Return, null, home));
                    if (validate(new(seat.ControllerId), retreat).Accepted) return retreat;
                }
                continue;
            }
            foreach (var target in targets.OrderBy(s => MapCoordinates.Distance(unit.Position, rules.Places[s.Id].Position)).ThenBy(s => s.Id).Take(4))
            {
                var command = new OrderUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.AttackSettlement, null, target.Id));
                if (validate(new(seat.ControllerId), command).Accepted && Reachable(unit.Position, rules.Formations[f], target.Id, unit.ServicePlaceId)) return command;
            }
        }
        foreach (var source in world.Settlements.Where(s => s.OwnerId == seat.ForceId && Controlled(s.CorpsId) && s.CanHostPersons &&
            s.Soldiers >= 6000 && s.Morale >= 65 && rules.Places.ContainsKey(s.Id)).OrderByDescending(s => s.Soldiers).ThenBy(s => s.Id).Take(4))
        {
            // Preserve a garrison and one administrator; a single officer is sufficient for this bounded policy.
            var people = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && p.SettlementId == source.Id &&
                !p.Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)).OrderBy(p => p.Status == OfficerStatus.Ruler).ThenBy(p => p.Id).ToArray();
            if (people.Length < 2) continue;
            foreach (var target in targets.OrderBy(s => MapCoordinates.Distance(rules.Places[source.Id].Position, rules.Places[s.Id].Position)).ThenBy(s => s.Id).Take(4))
            {
                if (world.Units.Any(u => u.OwnerId == seat.ForceId && u.Order is { Kind: MilitaryOrderKind.AttackSettlement } && u.Order.SettlementId == target.Id)) continue;
                foreach (var formation in rules.Formations.Values.Where(f => f.Attack is not null).OrderBy(f => f.DeploymentCost).ThenBy(f => f.Id).Take(32))
                {
                    var command = new DeployUnitCommand(commandId, seat.ForceId, world.Simulation.NextSequence, source.Id,
                        [people[0].Id], Math.Min(source.Soldiers - 3000, formation.SoldierCapacity), formation.Id,
                        new(MilitaryOrderKind.AttackSettlement, null, target.Id));
                    if (validate(new(seat.ControllerId), command).Accepted && Reachable(rules.Places[source.Id].Position, formation, target.Id, source.Id)) return command;
                }
            }
        }
        return null;

        bool Reachable(WorldPosition from, FormationRule formation, int target, int? source)
        {
            var key = (from, formation.Footprint, formation.TravelMode, formation.Attack!.Range, target, source);
            if (routes.TryGetValue(key, out bool reachable)) return reachable;
            var place = rules.Places[target];
            reachable = rules.Map.AdvanceIntoRange(from, new(place.Position, place.Footprint), formation.Footprint,
                formation.TravelMode, rules.Places.Values.Where(p => p.Id != source && p.Id != target &&
                    world.Settlements.Any(s => s.Id == p.Id && !s.IsDestroyed)).Select(p => new MapOccupancy(p.Position, p.Footprint)),
                0, formation.Attack!.Range).Reachable;
            routes.Add(key, reachable);
            return reachable;
        }
    }
}
