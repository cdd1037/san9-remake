using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonnelScenarioTests
{
    [Fact]
    public void PlayerLoadKeepsPendingDiscoveryRollAndDoesNotReexecutePreparation()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 11, 30)).Submission.Accepted);
        var task = Assert.Single(session.Snapshot.Simulation.Tasks);
        var loaded = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition), playerLoad: true);
        Assert.Equal(task, Assert.Single(loaded.Snapshot.Simulation.Tasks));
        Assert.NotEqual(session.Snapshot.Simulation.RunSeed, loaded.Snapshot.Simulation.RunSeed);
        loaded.BeginRecording();
        loaded.AdvanceXun();
        var replay = RecordingService.Replay(RecordingService.Encode(loaded.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(loaded), SaveService.Encode(replay.Session));
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "personnel-public", 190, 1, 1, 1,
        [new(1, "force", 1, 10000, 10000, 1000) { LeaderId = 1, BaseTrust = 1000 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(3, "destination", 1, 0, 15, 0, 100, 100, 0, [])],
        new() { [1] = "home", [3] = "destination", [30] = "region" }, new() { [1] = 1, [3] = 3, [30] = 1 })
    {
        UnbuiltPlaces = [new(30, San9.Core.Domain.MapCoordinates.Center(new(15, 15)))],
        Officers =
        [new("leader", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
         new("actor", new(11, 1, 1, new(50, 50, 100, 50), false)),
         new("hidden", new(21, null, null, new(50, 80, 50, 50), false)
         { Status = OfficerStatus.Undiscovered, RegionId = 30, Affinity = 50, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 0) })]
    };

    [Fact]
    public void YearOfSearchRecruitMoveAndRewardKeepsSavesQueriesAndReplayConsistent()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var resumed = SaveService.Decode(SaveService.Encode(session), definition);
        session.BeginRecording();
        HashSet<string> commands = [];
        List<DomainEvent> events = [];
        int checkpoints = 0;
        bool rewarded = false;
        bool appointed = false;
        bool sawTravel = false;
        for (int xun = 0; xun < 36; xun++)
        {
            var world = session.Snapshot;
            var target = world.Officers.Single(p => p.Id == 21);
            var actor = world.Officers.Single(p => p.Id == 11);
            bool Available(int id) => !world.Officers.Single(p => p.Id == id).Acted && !world.Simulation.Tasks.Any(t => t.OfficerId == id);
            Guid id = new((int)session.NextOperationSequence + 1, 0, 0, new byte[8]);
            GameCommand? command = target.Status == OfficerStatus.Undiscovered && Available(actor.Id)
                ? new SearchCommand(id, 1, world.Simulation.NextSequence, actor.Id, 30)
                : target.Status == OfficerStatus.Free && Available(actor.Id)
                ? new RecruitOfficerCommand(id, 1, world.Simulation.NextSequence, actor.Id, target.Id)
                : target.OwnerId == 1 && target.SettlementId == 1 && Available(target.Id)
                ? new MoveOfficersCommand(id, 1, world.Simulation.NextSequence, [target.Id], 3)
                : target.OwnerId == 1 && target.SettlementId == 3 && !appointed
                ? new AppointAdviserCommand(id, 1, world.Simulation.NextSequence, target.Id)
                : target.OwnerId == 1 && target.SettlementId == 3 && !rewarded
                ? new RewardOfficerCommand(id, 1, world.Simulation.NextSequence, target.Id) : null;
            if (command is not null)
            {
                var request = session.Request(new CommandOperation(command), id: id);
                var result = session.Apply(request);
                Assert.Equal(SessionDisposition.Accepted, result.Disposition);
                var second = resumed.Apply(request);
                Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(second.Events));
                Assert.Equal(SessionDisposition.Accepted, second.Disposition);
                commands.Add(command.GetType().Name);
                events.AddRange(result.Events);
                rewarded |= command is RewardOfficerCommand;
                appointed |= command is AppointAdviserCommand;
            }
            foreach (var view in session.View.Officers.Where(o => o.Journey is not null))
            {
                sawTravel = true;
                Assert.False(view.IsAvailable);
                var task = Assert.Single(session.Snapshot.Simulation.Tasks.Where(t => t.OfficerId == view.Id));
                Assert.Equal(task.Journey!.DestinationPlaceId, view.Journey!.DestinationPlaceId);
                Assert.Equal(task.Due, view.Journey.Due);
            }
            if (xun % 3 == 0)
            {
                resumed = SaveService.Decode(SaveService.Encode(resumed), definition);
                checkpoints++;
            }
            var advance = session.Request(new AdvanceOperation());
            var first = session.Apply(advance);
            var other = resumed.Apply(advance);
            Assert.Equal(SessionDisposition.Accepted, first.Disposition);
            Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(other.Events));
            events.AddRange(first.Events);
            Assert.Equal(SaveService.Encode(session), SaveService.Encode(resumed));
        }
        Assert.Equal(12, checkpoints);
        Assert.True(sawTravel);
        Assert.True(rewarded);
        Assert.Equal(5, commands.Count);
        Assert.True(appointed);
        Assert.Equal(21, session.Snapshot.Forces.Single().AdviserId);
        Assert.Contains(events, e => e is SearchResolvedEvent { DiscoveredOfficerId: 21, Recruited: false });
        Assert.Contains(events, e => e is RecruitmentResolvedEvent { TargetOfficerId: 21, Success: true });
        Assert.Contains(events, e => e is OfficerMovedEvent { OfficerId: 21, DestinationId: 3 });
        Assert.Contains(events, e => e is OfficerRewardedEvent { OfficerId: 21 });
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HomeSearchDiscoveryReturnsBeforeNextOrdersWithIdenticalSaveAndReplay(bool acceptRecruitment)
    {
        var baseline = Fixture();
        var definition = baseline with { Officers = baseline.Officers.Select(p => p.State.Id switch
        {
            11 => p with { State = p.State with { BaseAbilities = new(50, 50, 200, 50) } },
            21 => p with { State = p.State.WithLoyalty(1, 200) with { SettlementId = 1, RegionId = null } },
            _ => p
        }).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 11, 1)
            { AcceptRecruitment = acceptRecruitment }).Submission.Accepted);
        Assert.False(session.View.Officers.Single(p => p.Id == 11).IsAvailable);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        foreach (var operation in new SessionOperation[] { new EndControlOperation(), new AdvanceOperation() })
        {
            var request = session.Request(operation);
            var first = session.Apply(request);
            var second = restored.Apply(request);
            Assert.Equal(SessionDisposition.Accepted, first.Disposition);
            Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
        }
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(new GameClock(190, 1, 11), session.Snapshot.Clock);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.True(session.View.Officers.Single(p => p.Id == 11).IsAvailable);
        Assert.Equal(acceptRecruitment ? OfficerStatus.Serving : OfficerStatus.Free,
            session.Snapshot.Officers.Single(p => p.Id == 21).Status);
        if (acceptRecruitment) Assert.True(session.View.Officers.Single(p => p.Id == 21).IsAvailable);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }

    [Fact]
    public void RestoredMissionRejectsMalformedPayloadAndUnknownPlace()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        Guid id = Guid.NewGuid();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new CommandOperation(
            new SearchCommand(id, 1, 0, 11, 30)), id: id)).Disposition);
        var saved = session.Capture();
        var task = Assert.Single(saved.World.Simulation.Tasks);
        foreach (var journey in new PersonnelJourney?[] { null, new(9999, null, 20), new(30, 21, 20), new(30, null, null) })
        {
            var invalid = saved with { World = saved.World with { Simulation = saved.World.Simulation with { Tasks = [task with { Journey = journey }] } } };
            Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, invalid));
        }
    }
}
