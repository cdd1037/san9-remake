using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Bounded stage policy; every proposal uses the public command validator.</summary>
public static class DiplomacyPlanner
{
    public static GameCommand Choose(GameSession session, ControlSeat seat, Guid commandId) => session.ChooseDiplomacy(seat, commandId);

    internal static GameCommand Choose(SimulationEngine engine, ControlSeat seat, Guid commandId)
    {
        var world = engine.Snapshot;
        var context = new CommandContext(seat.ControllerId);
        long sequence = world.Simulation.NextSequence;
        var wait = new WaitCommand(commandId, seat.ForceId, sequence);
        var force = world.Forces.SingleOrDefault(f => f.Id == seat.ForceId && !f.IsEliminated);
        if (force is null || world.Simulation.Tasks.Any(t => t.ForceId == force.Id &&
            (t.Gift is not null || t.PrisonerReturn is not null || t.AttackRequest is not null || t.Surrender is not null ||
             t.Journey?.ReturnLeg?.DiplomaticEnvoy == true))) return wait;
        var abilities = engine.GetAbilities(world);
        var actors = world.Officers.Where(p => p.OwnerId == force.Id && p.IsServing && p.SettlementId is not null && !p.Acted &&
            !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)).OrderByDescending(p => abilities[p.Id].Politics).ThenBy(p => p.Id).Take(8).ToArray();
        var targets = world.Forces.Where(f => f.Id != force.Id && !f.IsEliminated && f.LeaderId is not null).OrderBy(f => f.Id).Take(32).ToArray();
        int Relation(int target) => world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(force.Id, target) &&
            r.SecondId == Math.Max(force.Id, target))?.Value ?? 0;
        foreach (var actor in actors)
        {
            foreach (var prisoner in world.Officers.Where(p => p.OwnerId == force.Id && p.Status == OfficerStatus.Captive).OrderBy(p => p.Id).Take(16))
            {
                int? captor = prisoner.SettlementId is int place ? world.Settlements.Single(s => s.Id == place).OwnerId :
                    prisoner.UnitId is int unit ? world.Units.Single(u => u.Id == unit).OwnerId : null;
                if (captor is not int target) continue;
                var command = new ReturnPrisonerCommand(commandId, force.Id, sequence, actor.Id, target, prisoner.Id);
                if (engine.Validate(context, command).Accepted) return command;
            }
            foreach (var target in targets)
            {
                var command = new DemandSurrenderCommand(commandId, force.Id, sequence, actor.Id, target.Id);
                if (engine.GetSurrenderAdvice(force.Id, actor.Id, target.Id).Opinion == AdviserOpinion.Favorable &&
                    engine.Validate(context, command).Accepted) return command;
            }
            foreach (var target in targets.Where(t => Relation(t.Id) >= 60 &&
                !world.Simulation.AttackRequests.Any(r => r.RequestedForceId == t.Id)))
                foreach (var place in world.Settlements.Where(s => !s.IsDestroyed && s.OwnerId is int owner && owner != force.Id &&
                    owner != target.Id && Relation(owner) < 40).OrderBy(s => s.Id).Take(16))
                {
                    var command = new RequestAttackCommand(commandId, force.Id, sequence, actor.Id, target.Id, place.Id);
                    if (engine.GetAttackRequestAdvice(force.Id, actor.Id, target.Id, place.Id).Opinion == AdviserOpinion.Favorable &&
                        engine.Validate(context, command).Accepted) return command;
                }
            if (force.Money < 20000) continue;
            foreach (var target in targets.Where(t => Relation(t.Id) is >= 40 and < 80).OrderByDescending(t => Relation(t.Id)))
            {
                var command = new GiftCommand(commandId, force.Id, sequence, actor.Id, target.Id, new(GiftKind.Money, 2000, null));
                if (engine.Validate(context, command).Accepted) return command;
            }
        }
        return wait;
    }
}
