using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AmphibiousSessionTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "amphibious", 190, 1, 1, 1,
        [new(1, "force", 1, 100)],
        [new(1, "origin", 1, 0, 1, 1000, 100, 100, 70, []), new(2, "destination", 1, 7, 1, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(8, 3, Enumerable.Range(0, 24).Select(i => i % 8 is >= 2 and <= 4 ? 1 : 0).ToImmutableArray(),
            [new(0, 2, null), new(1, null, 3) { IsWater = true }],
            [new(100, 2, 50, 10000, 5, new(1, 1), TravelMode.Land) { DefaultNavalFormationId = 200 },
             new(200, 3, 0, 10000, 5, new(1, 1), TravelMode.Water),
             new(300, 6, 100, 10000, 5, new(3, 3), TravelMode.Water)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]),
        Information = new([], [], [], [new CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }], [])
    };

    [Fact]
    public void NavalSelectionMovementForecastSaveAndReplayShareTheSameState()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 100,
            new(MilitaryOrderKind.Move, new(32, 7), null))).Submission.Accepted);
        var unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(200, unit.NavalFormationId);
        var forecast = session.View.UnitArrival(unit.Id);
        Assert.Equal(ArrivalEstimateStatus.Known, forecast.Status);
        session.AdvanceXun();
        unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(200, session.View.FormationId(unit));
        Assert.Equal(forecast.Days - 10, session.View.UnitArrival(unit.Id).Days);
        var before = session.Capture().StateHash;
        Assert.Equal(CommandRejection.InvalidTask, session.Execute(new("player"), new ReformUnitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, unit.Id, 100, [1], unit.Order) { NavalFormationId = 100 }).Submission.Rejection);
        Assert.Equal(before, session.Capture().StateHash);
        Assert.True(session.Execute(new("player"), new ReformUnitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, unit.Id, 100, [1], unit.Order) { NavalFormationId = 300 }).Submission.Accepted);
        Assert.Equal(300, session.View.FormationId(Assert.Single(session.Snapshot.Units)));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(session.View.UnitArrival(unit.Id), restored.View.UnitArrival(unit.Id));
        for (int i = 0; i < 5 && session.Snapshot.Units.Single().Order is not null; i++)
        {
            Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        }
        unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(new WorldPosition(32, 7), unit.Position);
        Assert.Equal(100, session.View.FormationId(unit));
        Assert.Equal(300, unit.NavalFormationId);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }
}
