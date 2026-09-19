using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficeAssignmentTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(null, false)]
    public void LowestDefinedRankIsNotAnAbsentRank(int? rankId, bool allowed)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Information = baseline.Information with
            {
                Ranks = [new(8, "rank.lowest", 20000, 0, 0, 0)],
                Offices = [new(400, "office", 10000, 0) { RequiredRankId = 8 }]
            },
            Forces = baseline.Forces.Select(f => f with { RankId = rankId }).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        Assert.Equal(rankId, session.View.Forces.Single(f => f.Id == 1).RankId);
        Assert.Equal(8, Assert.Single(session.View.Information.Ranks).Id);
        session.BeginRecording();
        var result = session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(2, 400)]));
        Assert.Equal(allowed, result.Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with
        { Forces = definition.Forces.Select(f => f with { RankId = allowed ? null : 8 }).ToArray() }));
    }

    [Fact]
    public void RankGateUsesExplicitPrecedenceEvenWhenTrustThresholdsRunInOppositeOrder()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Information = baseline.Information with
        {
            Ranks = [new(9000, "rank.low", 10000, 900, 0, 10), new(3, "rank.high", 20000, 100, 0, 30)],
            Offices = [new(400, "office", 20000, 9) { RequiredRankId = 3, AbilityBonuses = new(0, 0, 7, 0) }]
        }, Forces = baseline.Forces.Select(f => f with { RankId = 9000 }).ToArray() };
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with
        { Information = definition.Information with { Ranks = definition.Information.Ranks.Select(r => r with { Precedence = r.Precedence + 1 }).ToArray() } }));
        var session = GameSession.Start(definition, 190);
        string before = session.Snapshot.StateHash;
        Assert.False(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(2, 400)])).Submission.Accepted);
        Assert.Equal(before, session.Snapshot.StateHash);
        definition = definition with { Forces = definition.Forces.Select(f => f with { RankId = 3 }).ToArray() };
        session = GameSession.Start(definition, 190);
        Assert.True(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(2, 400)])).Submission.Accepted);
        var snapshot = session.Capture();
        var invalid = snapshot with { World = snapshot.World with { Officers = snapshot.World.Officers
            .Select(p => p.Id == 2 ? p with { OfficeId = 999999 } : p).ToArray() } };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, invalid));
        Assert.True(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 1, [new(2, null)])).Submission.Accepted);
        var person = session.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Null(person.OfficeId);
        Assert.Equal(person.BaseAbilities, session.View.Officers.Single(p => p.Id == person.Id).Abilities);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), definition)));
    }

    [Fact]
    public void AtomicOfficeSwapUpdatesAbilitiesAndLoyaltyAndSurvivesSaveReplay()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Information = baseline.Information with { Offices = [
                new(900, "office.a", 15000, 5) { AbilityBonuses = new(0, 0, 5, 0) },
                new(1200, "office.b", 20000, 8) { AbilityBonuses = new(0, 0, 0, 8) }] },
            Officers = baseline.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State.WithLoyalty(1, 80) with
                { OfficeId = 900 } } : p.State.Id == 3 ? p with { State = p.State.WithLoyalty(1, 90) with
                { OfficeId = 1200 } } : p).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        string before = session.Snapshot.StateHash;
        Assert.False(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(2, 1200)])).Submission.Accepted);
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.True(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0,
            [new(2, 1200), new(3, 900)])).Submission.Accepted);
        var person = session.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(82, person.Loyalty);
        Assert.Equal(person.BaseAbilities.Leadership + 8, session.View.Officers.Single(p => p.Id == person.Id).Abilities.Leadership);
        Assert.False(person.Acted);
        Assert.Equal(88, session.Snapshot.Officers.Single(p => p.Id == 3).Loyalty);
        var saved = SaveService.Encode(session);
        Assert.Equal(saved, SaveService.Encode(SaveService.Decode(saved, definition)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(saved, SaveService.Encode(replay.Session));
    }
}
