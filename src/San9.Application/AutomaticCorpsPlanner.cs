using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Bounded personnel, reinforcement and economic work for automatic controllers.</summary>
internal static class AutomaticCorpsPlanner
{
    internal static void Run(SimulationEngine engine, ControlSeat active, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (active.Kind == ControllerKind.Automatic)
        {
            var diplomacy = DiplomacyPlanner.Choose(engine, active, Id(engine.Snapshot, -1, "diplomacy"));
            if (diplomacy is not WaitCommand) events.AddRange(engine.Execute(new(active.ControllerId), diplomacy).Events);
            var command = MilitaryPlanner.Choose(engine.Snapshot, engine.Validate, military, active,
                Id(engine.Snapshot, -1, "military"));
            if (command is not WaitCommand) events.AddRange(engine.Execute(new(active.ControllerId), command).Events);
        }
        foreach (var corps in engine.Snapshot.Corps.Where(c => c.OwnerId == active.ForceId &&
            c.ControlMode == CorpsControlMode.Automatic).OrderBy(c => c.Id))
        {
            var seat = active with { ControllerId = CorpsControl.ControllerId(corps.Id), Kind = ControllerKind.Automatic };
            var context = new CommandContext(seat.ControllerId);
            var reinforcement = MilitaryPlanner.Choose(engine.Snapshot, engine.Validate, military, seat,
                Id(engine.Snapshot, corps.Id, "military"), corps.Id);
            if (reinforcement is not WaitCommand) events.AddRange(engine.Execute(context, reinforcement).Events);
            var snapshot = engine.Snapshot;
            var places = snapshot.Settlements.Where(s => s.CorpsId == corps.Id && s.CanHostPersons && s.Durability > 0).Select(s => s.Id).ToHashSet();
            var abilities = engine.GetAbilities(snapshot);
            var actor = snapshot.Officers.Where(p => p.IsServing && p.OwnerId == corps.OwnerId &&
                p.SettlementId is int home && places.Contains(home) && !p.Acted && !snapshot.Simulation.Tasks.Any(t => t.OfficerId == p.Id))
                .OrderByDescending(p => abilities[p.Id].Politics).ThenBy(p => p.Id).FirstOrDefault();
            if (actor is not null)
            {
                foreach (var target in snapshot.Officers.Where(p => p.IsVisible && p.OwnerId != corps.OwnerId &&
                    p.Status is OfficerStatus.Free or OfficerStatus.Captive && p.SettlementId is int place && places.Contains(place) &&
                    p.LoyaltyTo(corps.OwnerId) >= 50).OrderBy(p => p.Id).Take(16))
                {
                    var command = new RecruitOfficerCommand(Id(engine.Snapshot, corps.Id, "personnel"), corps.OwnerId,
                        engine.Snapshot.Simulation.NextSequence, actor.Id, target.Id);
                    if (!engine.Validate(context, command).Accepted) continue;
                    events.AddRange(engine.Execute(context, command).Events);
                    break;
                }
            }
            var development = DomesticPlanner.Choose(engine.Snapshot, engine.Validate, seat, Id(engine.Snapshot, corps.Id, "domestic"),
                (int)((engine.Snapshot.Clock.DayNumber - engine.Snapshot.Simulation.Started.DayNumber) / 10), corpsId: corps.Id);
            if (development is not WaitCommand) events.AddRange(engine.Execute(context, development).Events);
        }
    }

    private static Guid Id(WorldSnapshot snapshot, int corpsId, string work) => new(Convert.FromHexString(CanonicalJson.Hash(new
        { snapshot.Simulation.InitialSeed, snapshot.Clock, snapshot.Simulation.NextSequence, CorpsId = corpsId, Work = work })).AsSpan(0, 16));
}
