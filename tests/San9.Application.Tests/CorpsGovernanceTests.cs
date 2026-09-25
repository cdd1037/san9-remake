using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CorpsGovernanceTests
{
    [Fact]
    public void DissolvedCorpsCanBeSavedRestoredAndReplayed()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Units = [new(10, 1, new(400, 500), 100) { CorpsId = 11, ServicePlaceId = 3, IsPersonnel = true, Condition = UnitCondition.Routed, OrderClass = UnitOrderClass.NonMilitary }],
            Settlements = [baseline.Settlements[0] with { CorpsId = 10 }, baseline.Settlements[1],
                baseline.Settlements[0] with { Id = 3, CorpsId = 11 }],
            Officers = baseline.Officers.Select(p => p.State.Id == 2 ? p with
                { State = p.State with { SettlementId = 3, Status = OfficerStatus.Governor } } : p).ToArray(),
            Information = baseline.Information with { Corps = [.. baseline.Information.Corps.Where(c => c.OwnerId != 1), new(10, 1, 1, new(1000, 2000)) { IsRoot = true }, new(11, 1, 2, new(100, 200))] }
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var command = new DissolveCorpsCommand(Guid.NewGuid(), 1, 0, 11);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new CommandOperation(command), id: command.CommandId)).Disposition);
        Assert.Equal(3, Assert.Single(session.Snapshot.Units).ServicePlaceId);
        Assert.Equal(new WorldPosition(400, 500), Assert.Single(session.Snapshot.Units).Position);
        var save = SaveService.Encode(session);
        Assert.Equal(save, SaveService.Encode(SaveService.Decode(save, definition)));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(save, SaveService.Encode(replay.Session));
    }

    [Fact]
    public void CreateThenDissolvePreservesResourcesAndSupportsDynamicIdentitySaveReplay()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            RegionNameKeys = new() { [1] = "city.1", [3] = "city.3" },
            RegionCityIds = new() { [1] = 1, [3] = 3 },
            Settlements = [baseline.Settlements[0] with { CorpsId = 1000 }, baseline.Settlements[1],
                baseline.Settlements[0] with { Id = 3, CorpsId = 1000 }],
            Officers = baseline.Officers.Select(p => p.State.Id == 2 ? p with
                { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            Units = [new(20, 1, new(400, 500), 100) { CorpsId = 1000, ServicePlaceId = 3, OrderClass = UnitOrderClass.Military },
                new(21, 1, new(400, 500), 100) { CorpsId = 1000, ServicePlaceId = 3, IsEmbedded = true, IsPersonnel = true }],
            Information = baseline.Information with { Corps = [.. baseline.Information.Corps.Where(c => c.OwnerId != 1), new(1000, 1, 1, new(1000, 2000)) { IsRoot = true }] }
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var before = session.Snapshot;
        Assert.True(session.Execute(new("player"), new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [3], 100, 200)).Submission.Accepted);
        var created = session.Snapshot.Corps.Single(c => !c.IsRoot);
        Assert.Equal(1001, created.Id);
        Assert.Equal(2, created.LeaderId);
        Assert.Equal(1001, session.Snapshot.Units.Single(u => u.Id == 20).CorpsId);
        Assert.Equal(1000, session.Snapshot.Units.Single(u => u.Id == 21).CorpsId);
        var save = SaveService.Encode(session);
        Assert.Equal(save, SaveService.Encode(SaveService.Decode(save, definition)));
        Assert.True(session.Execute(new("player"), new ChangeCorpsCommand(Guid.NewGuid(), 1, 1, created.Id, [3], 50, 300)).Submission.Accepted);
        Assert.Equal(created.Id, session.Snapshot.Units.Single(u => u.Id == 21).CorpsId);
        var changedSave = SaveService.Encode(session);
        var restored = SaveService.Decode(changedSave, definition);
        Assert.Equal(changedSave, SaveService.Encode(restored));
        var dissolve = new DissolveCorpsCommand(Guid.NewGuid(), 1, 2, created.Id);
        Assert.True(session.Execute(new("player"), dissolve).Submission.Accepted);
        Assert.True(restored.Execute(new("player"), dissolve).Submission.Accepted);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(before.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money, session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        Assert.Equal(before.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Food, session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Food);
        Assert.Single(session.Snapshot.Corps, c => c.OwnerId == 1);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
    }
}
