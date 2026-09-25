using San9.Application.AI;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DiplomaticReturnPlannerTests
{
    [Fact]
    public void ReturningDiplomaticEnvoyReservesBudgetAcrossRestore()
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { X = 20 } : s).ToArray(),
            Information = basis.Information with { Corps = basis.Information.Corps.Select(c => c.IsRoot && c.OwnerId == 1 ? c with { Treasury = c.Treasury with { Money = 30000 } } : c).ToArray(), Relations = [new(1, 2, 70)] }
        };
        var session = GameSession.Start(content, 42);
        var seat = session.Snapshot.Simulation.Controls[0];
        var first = new GiftCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 2,
            new(GiftKind.Money, 1, null));
        Assert.True(session.Execute(new(seat.ControllerId), first).Submission.Accepted);
        session.AdvanceXun();
        Assert.Contains(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 2 && t.Journey?.ReturnLeg is not null);
        // Either refusal gives 65 or acceptance gives 90. Restore a valid gift target relation independently.
        var saved = session.CaptureStableState();
        var engine = new SimulationEngine(WorldState.Restore(saved with { Relations = [new(1, 2, 65)] }), EconomyRulebook.Empty,
            personnel: new([new(1, content.Settlements[0].Position), new(2, content.Settlements[1].Position)]));
        Assert.IsType<WaitCommand>(DiplomacyPlanner.Choose(engine, seat, Guid.NewGuid()));
        var resumed = engine.CaptureStableState();
        var ordinaryReturns = resumed.Simulation.Tasks.Select(t => t with
        {
            Journey = t.Journey! with { ReturnLeg = t.Journey.ReturnLeg! with { DiplomaticEnvoy = false } }
        }).ToImmutableArray();
        var ordinary = new SimulationEngine(WorldState.Restore(resumed with
        {
            Simulation = resumed.Simulation with { Tasks = ordinaryReturns }
        }), EconomyRulebook.Empty, personnel: new([new(1, content.Settlements[0].Position), new(2, content.Settlements[1].Position)]));
        Assert.IsType<GiftCommand>(DiplomacyPlanner.Choose(ordinary, seat, Guid.NewGuid()));
    }
}
