using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Bounded personnel, reinforcement and economic work for automatic controllers.</summary>
internal static class AutomaticTurnRunner
{
    internal static void EndControl(SimulationEngine engine, CommandContext context, MilitaryRulebook? military, List<DomainEvent> events, ref AiSessionState ai)
    {
        var state = engine.Snapshot.Simulation;
        if (state.Boundary != SimulationBoundary.Stable || state.ActiveControl >= state.Controls.Length ||
            state.Controls[state.ActiveControl].ControllerId != context.ControllerId)
            throw new InvalidOperationException("Not the active stable controller.");
        Run(engine, state.Controls[state.ActiveControl], military, events, ref ai);
        events.AddRange(engine.EndControl(context).Events);
    }

    private static void Run(SimulationEngine engine, ControlSeat active, MilitaryRulebook? military, List<DomainEvent> events, ref AiSessionState ai)
    {
        if (active.Kind == ControllerKind.Automatic)
        {
            var diplomacy = DiplomacyPlanner.Choose(engine, active, Id(engine.Snapshot, -1, "diplomacy"));
            if (diplomacy is not WaitCommand) events.AddRange(engine.Execute(new(active.ControllerId), diplomacy).Events);
            var command = MilitaryPlanner.Choose(engine.Snapshot, engine.Validate, military, active,
                Id(engine.Snapshot, -1, "military"));
            if (command is not WaitCommand) events.AddRange(engine.Execute(new(active.ControllerId), command).Events);
            var strategy = StrategyPlanner.Choose(engine.Snapshot, engine.GetPreparationEligibility, military, active,
                Id(engine.Snapshot, -1, "strategy"));
            if (strategy is not WaitCommand) events.AddRange(engine.Execute(new(active.ControllerId), strategy).Events);
            Recruit(active, new(active.ControllerId), -1);
            ai = EconomicTurnRunner.Run(engine, active, new(active.ControllerId), ai, work => Id(engine.Snapshot, -1, work), events);
        }
        foreach (var corps in engine.Snapshot.Corps.Where(c => c.OwnerId == active.ForceId &&
            c.ControlMode == CorpsControlMode.Automatic && !(active.Kind == ControllerKind.Automatic && c.IsRoot)).OrderBy(c => c.Id))
        {
            var seat = active with { ControllerId = CorpsControl.ControllerId(corps.Id), Kind = ControllerKind.Automatic };
            var context = new CommandContext(seat.ControllerId);
            var reinforcement = MilitaryPlanner.Choose(engine.Snapshot, engine.Validate, military, seat,
                Id(engine.Snapshot, corps.Id, "military"), corps.Id);
            if (reinforcement is not WaitCommand) events.AddRange(engine.Execute(context, reinforcement).Events);
            var strategy = StrategyPlanner.Choose(engine.Snapshot, engine.GetPreparationEligibility, military, seat,
                Id(engine.Snapshot, corps.Id, "strategy"));
            if (strategy is not WaitCommand) events.AddRange(engine.Execute(context, strategy).Events);
            Recruit(active, context, corps.Id);
            ai = EconomicTurnRunner.Run(engine, active, context, ai, work => Id(engine.Snapshot, corps.Id, work), events);
        }

        void Recruit(ControlSeat owner, CommandContext context, int scope)
        {
            var snapshot = AiPlanningWorld.Read(engine.Snapshot);
            var places = snapshot.Settlements.Where(s => s.CanHostPersons && s.Durability > 0 &&
                CorpsControl.CanControlSettlement(snapshot, owner, context, s.Id)).Select(s => s.Id).ToHashSet();
            var abilities = engine.GetAbilities(snapshot);
            var actor = snapshot.Officers.Where(p => p.IsServing && p.OwnerId == owner.ForceId &&
                p.SettlementId is int home && places.Contains(home) && !p.Acted && !snapshot.Simulation.Tasks.Any(t => t.OfficerId == p.Id))
                .OrderByDescending(p => abilities[p.Id].Politics).ThenBy(p => p.Id).FirstOrDefault();
            if (actor is not null)
            {
                foreach (var target in snapshot.Officers.Where(p => p.IsVisible && p.OwnerId != owner.ForceId &&
                    p.Status is OfficerStatus.Free or OfficerStatus.Captive && p.SettlementId is int place && places.Contains(place) &&
                    p.LoyaltyTo(owner.ForceId) >= 50).OrderBy(p => p.Id).Take(16))
                {
                    var command = new RecruitOfficerCommand(Id(engine.Snapshot, scope, "personnel"), owner.ForceId,
                        engine.Snapshot.Simulation.NextSequence, actor.Id, target.Id);
                    if (!engine.Validate(context, command).Accepted) continue;
                    events.AddRange(engine.Execute(context, command).Events);
                    break;
                }
            }
        }
    }

    private static Guid Id(WorldSnapshot snapshot, int corpsId, string work) => new(Convert.FromHexString(CanonicalJson.Hash(new
        { snapshot.Simulation.InitialSeed, snapshot.Clock, snapshot.Simulation.NextSequence, CorpsId = corpsId, Work = work })).AsSpan(0, 16));
}
