using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Bounded reinforcement policy; commands remain subject to the shared world validator.</summary>
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
}
