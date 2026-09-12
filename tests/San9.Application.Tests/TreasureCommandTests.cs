using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class TreasureCommandTests
{
    private static ScenarioInitialState Fixture() => new(22, "treasure-commands", 190, 1, 1, 1,
        [new(1, "force", 1, 1000, 1000, 1000) { LeaderId = 1, BaseTrust = 950 }],
        [new(1, "city", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "city" }, new() { [1] = 1 })
    {
        ImperialSealId = 10,
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Ruler, ItemBonuses = new(10, 0, 0, 0) }),
            new("recipient", new OfficerState(2, 1, 1, new(50, 50, 50, 50), false).WithLoyalty(1, 98))],
        Information = new([], [], [new(10, "treasure", 1, 10, 8, null) { AbilityBonuses = new(10, 0, 0, 0) }], [],
            [new(10, 1, TreasureDiscovery.Discovered, null)])
    };

    [Fact]
    public void TransferCommandsKeepQueriesSaveRestoreAndRecordingConsistent()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var id = Guid.NewGuid();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new CommandOperation(
            new GrantTreasureCommand(id, 1, 0, 10, 2)), id: id)).Disposition);
        var texts = new TextCatalog(3, new[] { "treasure", "recipient", "ruler" }
            .ToDictionary(k => k, k => new TextEntry(k, k)));
        Assert.Empty(TreasureCatalog.Read(session.View, texts, holderId: 1).Rows);
        Assert.Single(TreasureCatalog.Read(session.View, texts, holderId: 2).Rows);
        Assert.Equal(60, session.Snapshot.Officers.Single(p => p.Id == 2).Abilities.War);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        id = Guid.NewGuid();
        var request = session.Request(new CommandOperation(new ConfiscateTreasureCommand(id, 1, 1, 10)), id: id);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Apply(request).Events));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        Assert.Equal(94, session.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        Assert.Equal(1, definition.Information.TreasureStates.Single().HolderId);
        Assert.Equal(default, definition.Officers.Single(p => p.State.Id == 2).State.ItemBonuses);
    }

    [Fact]
    public void RestoreRejectsHolderChangesWithoutRecomputedBonuses()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var state = session.Capture();
        var invalid = state with { World = state.World with
        { Treasures = [state.World.Treasures.Single() with { HolderId = 2 }] } };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, invalid));
        var initial = definition with { Information = definition.Information with
        { TreasureStates = [definition.Information.TreasureStates.Single() with { HolderId = 2 }] } };
        Assert.Throws<InvalidDataException>(initial.Validate);
    }
}
