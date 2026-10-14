using San9.Tests;
using San9.Content.Scenarios;
using San9.Application.Presentation;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ConstructionSessionTests
{
    [Fact]
    public void NewlyConstructedFacilityParticipatesInSessionQueriesAndStableRestore()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "construction", 190, 1, 1, 1,
            [new(1, "force", 1, 100) { LeaderId = 11 }],
            [new(1, "city", 1, 0, 0, 20000, 600, 600, 100, [])],
            new() { [1] = "city", [8000] = "region" }, new() { [1] = 1, [8000] = 1 })
        {
            FacilityNameSuffixKeys = new() { [9000] = "fort", [9001] = "work" },
            Officers = [new("officer", new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler })],
            UnbuiltPlaces = [new(8000, new(17, 2))],
            Military = new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
                [new(1, 5, 50, 100000, 1, new(1, 1), TravelMode.Land)],
                [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 }])
            {
                ConstructionFormations = new(1, null),
                Facilities = [new(0, 0, 600, new(1, 1), 15, false, true, true, 500000, null, null, null, []),
                    new(9000, 1000, 600, new(3, 3), 15, true, true, true, 250000, null, null, null, []),
                    new(9001, 500, 100, new(1, 1), 0, true, false, false, 250000, null, null, null, [])],
                ConstructionSites = [new(8000, new(17, 2), [9000]), new(8000, new(22, 2), [9001])]
            }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }], []) };
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new()
        { ["city"] = "都市", ["region"] = "白水", ["fort"] = "城塞", ["work"] = "土垒" });
        Assert.Throws<InvalidDataException>(() => (definition with { FacilityNameSuffixKeys = new() { [99999] = "fort" } }).Validate());
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        definition.FacilityNameSuffixKeys[9000] = "changed-by-author";
        Assert.True(session.Execute(new("player"), new ConstructFacilityCommand(Guid.NewGuid(), 1, 0, 1, [11], 10000,
            8000, 9000, new(17, 2))).Submission.Accepted);
        session.AdvanceXun();
        var building = session.View.Settlements.Single(s => s.Id == 8000);
        Assert.True(building.IsUnderConstruction);
        Assert.Equal(9000, building.FacilityTypeId);
        Assert.Equal("白水城塞", building.ResolveName(texts));
        Assert.Equal("都市", session.View.Settlements.Single(s => s.Id == 1).ResolveName(texts));
        definition.FacilityNameSuffixKeys[9000] = "fort";
        Assert.Equal(3, building.X);
        Assert.Empty(ConstructionLocations.Read(session, 1, 9000));
        var restored = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        for (int i = 0; i < 3; i++)
        {
            EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        }
        Assert.False(restored.View.Settlements.Single(s => s.Id == 8000).IsUnderConstruction);
        Assert.Equal(10000u, restored.View.Settlements.Single(s => s.Id == 8000).Soldiers);
        Assert.Equal(8000, restored.View.Officers.Single().SettlementId);
        Assert.Equal("白水城塞", restored.View.Settlements.Single(s => s.Id == 8000).ResolveName(texts));
        var roster = new StrategyRoster();
        roster.Refresh(restored.View, 1);
        var entry = roster.Facilities.Pinned ?? roster.Facilities.Ordinary.Single();
        Assert.Equal("白水城塞", entry.ResolveName(texts));
        Assert.True(session.Execute(new("player"), new DemolishFacilityCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 8000)).Submission.Accepted);
        Assert.True(session.View.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(1, session.View.Officers.Single().SettlementId);
        Assert.Single(ConstructionLocations.Read(session, 1, 9001));
        Assert.True(session.Execute(new("player"), new ConstructFacilityCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, [11], 5000, 8000, 9001, new(22, 2))).Submission.Accepted);
        var reconstruction = session.AdvanceXun();
        Assert.Single(reconstruction.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        var rebuilt = session.View.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(9001, rebuilt.FacilityTypeId);
        Assert.Equal(4, rebuilt.X);
        Assert.Equal("白水土垒", rebuilt.ResolveName(texts));
        Assert.False(rebuilt.IsUnderConstruction);
        Assert.False(rebuilt.IsDestroyed);
        Assert.False(rebuilt.IsFacilityListEntry);
        Assert.Equal(0u, rebuilt.Soldiers);
        var loaded = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal("白水土垒", loaded.View.Settlements.Single(s => s.Id == 8000).ResolveName(texts));
        StateAssert.Equal(session.Snapshot, loaded.Snapshot);
        var replay = RecordingService.Replay(RecordingService.Encode(session.CaptureRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
