using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class GovernanceDepartureTests
{
    private static SimulationEngine Engine(bool successor)
    {
        var people = new List<OfficerState>
        {
            new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
            new(2, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Governor, IsCommander = true }
        };
        if (successor) people.Add(new(3, 1, 2, new(50, 50, 50, 80), false));
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 10000, 10000, 0) { LeaderId = 1 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 11 }], people,
            corps: [new(10, 1, 1, null), new(11, 1, 2, new(100, 200))],
            controls: [new("player", 1, ControllerKind.Player)]);
        return new(world, EconomyRulebook.Empty, new PersonnelRulebook([new(1, new(0, 0)), new(2, new(30, 0))],
            new Dictionary<int, int> { [1] = 1, [2] = 2 }, [new(1, 2), new(2, 1)]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GovernorDepartureReplacesLeadershipOrMergesAnEmptyCorps(bool move, bool successor)
    {
        var engine = Engine(successor);
        var before = engine.Snapshot;
        GameCommand command = move ? new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [2], 1)
            : new ExileOfficerCommand(Guid.NewGuid(), 1, 0, 2);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        if (move)
        {
            Assert.Equal(11, engine.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
            engine.EndControl(new("player"));
            engine.AdvanceXun();
        }
        if (successor)
        {
            Assert.Equal(3, engine.Snapshot.Corps.Single(c => c.Id == 11).LeaderId);
            Assert.True(engine.Snapshot.Officers.Single(p => p.Id == 3).IsCommander);
        }
        else
        {
            Assert.Single(engine.Snapshot.Corps);
            Assert.Equal(10, engine.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
            if (!move) Assert.Equal(before.Forces.Single().Money + before.Corps.Single(c => c.Id == 11).Treasury!.Money,
                engine.Snapshot.Forces.Single().Money);
        }
        Assert.Equal(move ? OfficerStatus.Serving : OfficerStatus.Free, engine.Snapshot.Officers.Single(p => p.Id == 2).Status);
    }
}
