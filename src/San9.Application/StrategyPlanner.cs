using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Bounded stage policy. Planning reads identities and own resources; execution owns all effects.</summary>
public static class StrategyPlanner
{
    public static GameCommand Choose(GameSession session, ControlSeat seat, Guid commandId) =>
        Choose(session.Snapshot, session.Validate, session.MilitaryRules, seat, commandId);

    internal static GameCommand Choose(WorldSnapshot world,
        Func<CommandContext, GameCommand, CommandSubmissionResult> validate, MilitaryRulebook? rules,
        ControlSeat seat, Guid commandId, int? corpsId = null)
    {
        long sequence = world.Simulation.NextSequence;
        var wait = new WaitCommand(commandId, seat.ForceId, sequence);
        if (rules is null || world.Simulation.Tasks.Any(t => t.ControllerId == seat.ControllerId &&
            (t.Journey?.UnitStrategy is not null || t.Journey?.Burn is not null || t.Journey?.Seize is not null ||
             t.Journey?.Gossip is not null || t.Kind == "strategy-alienate"))) return wait;
        bool Controlled(SettlementState place) => corpsId is int id ? place.CorpsId == id : place.CorpsId is null ||
            world.Corps.Any(c => c.Id == place.CorpsId && c.ControlMode == CorpsControlMode.Direct);
        var homes = world.Settlements.Where(s => s.OwnerId == seat.ForceId && Controlled(s)).ToDictionary(s => s.Id);
        var actors = world.Officers.Where(p => p.OwnerId == seat.ForceId && p.IsServing && !p.Acted &&
            p.SettlementId is int home && homes.ContainsKey(home) && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id))
            .OrderBy(p => p.Status is OfficerStatus.Ruler or OfficerStatus.Governor).ThenBy(p => p.Id).Take(8).ToArray();
        bool Enemy(int force) => force != seat.ForceId && world.Forces.Any(f => f.Id == force && !f.IsEliminated) &&
            (world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(force, seat.ForceId) &&
                r.SecondId == Math.Max(force, seat.ForceId))?.Value ?? 0) < 40;
        // Enemy field identities come only from our current contact/order, never a scan of hidden positions.
        var contacts = world.Units.Where(u => u.OwnerId == seat.ForceId)
            .SelectMany(u => new[] { u.EngagementTargetId, u.Order?.UnitId }).OfType<int>().ToHashSet();
        var units = world.Units.Where(u => u.OwnerId == seat.ForceId || contacts.Contains(u.Id) && Enemy(u.OwnerId))
            .OrderBy(u => u.Id).Take(16).ToArray();
        var places = world.Settlements.Where(s => s.OwnerId is int owner && Enemy(owner) && !s.IsDestroyed)
            .OrderBy(s => s.Id).Take(16).ToArray();
        var people = world.Officers.Where(p => p.IsVisible && p.OwnerId is int owner && Enemy(owner) &&
            (p.SettlementId is not null || p.UnitId is int unit && contacts.Contains(unit)))
            .OrderBy(p => p.Id).Take(16).ToArray();
        var forces = world.Forces.Where(f => Enemy(f.Id)).OrderBy(f => f.Id).Take(16).ToArray();
        // Rotate the first category by calendar, so low-priority domains are not permanently starved.
        int first = (int)((world.Clock.DayNumber / 10 + seat.ForceId) % 8);
        for (int offset = 0; offset < 8; offset++)
        {
            int kind = (first + offset) % 8;
            foreach (var actor in actors)
                foreach (var command in Candidates(kind, actor))
                    if (validate(new(seat.ControllerId), command).Accepted) return command;
        }
        return wait;

        IEnumerable<GameCommand> Candidates(int kind, OfficerState actor)
        {
            if (kind < 4)
            {
                var strategy = (UnitStrategyKind)kind;
                foreach (var target in units)
                {
                    if (kind < 2 ? target.OwnerId == seat.ForceId : target.OwnerId != seat.ForceId) continue;
                    if (strategy == UnitStrategyKind.Reinforce &&
                        (homes[actor.SettlementId!.Value].Soldiers < 6000 || target.FormationId is not int formation ||
                         !rules.Formations.TryGetValue(formation, out var definition) ||
                         (long)definition.SoldierCapacity - target.Soldiers < 1000)) continue;
                    yield return new UnitStrategyCommand(commandId, seat.ForceId, sequence, actor.Id, strategy, target.Id);
                }
            }
            else if (kind is 4 or 5)
            {
                foreach (var target in places)
                    yield return kind == 4
                        ? new BurnCommand(commandId, seat.ForceId, sequence, actor.Id, target.Id)
                        : new SeizeCommand(commandId, seat.ForceId, sequence, actor.Id, target.Id);
            }
            else if (kind == 6)
            {
                foreach (var target in people)
                    yield return new AlienateCommand(commandId, seat.ForceId, sequence, actor.Id, target.Id);
            }
            else
            {
                foreach (var target in forces)
                    foreach (var other in forces.Where(f => f.Id != target.Id))
                        yield return new GossipCommand(commandId, seat.ForceId, sequence, actor.Id, target.Id, other.Id);
            }
        }
    }
}
