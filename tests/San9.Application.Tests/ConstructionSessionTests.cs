using San9.Content.Scenarios;
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
            [new(1, "force", 1, 10000, 1000000, 100) { LeaderId = 11 }],
            [new(1, "city", 1, 0, 0, 20000, 600, 600, 100, [])],
            new() { [1] = "city", [8000] = "region" }, new() { [1] = 1, [8000] = 1 })
        {
            Officers = [new("officer", new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler })],
            UnbuiltPlaces = [new(8000, new(17, 2))],
            Military = new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
                [new(1, 5, 50, 100000, 1, new(1, 1), TravelMode.Land)],
                [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 }])
            {
                Facilities = [new(0, 0, 600, new(1, 1), 15, false, true, true, 500000, null, null, null, []),
                    new(9000, 1000, 600, new(3, 3), 15, true, true, true, 250000, null, null, null, [])],
                ConstructionSites = [new(8000, new(17, 2), [9000])]
            }
        };
        var session = GameSession.Start(definition, 42);
        Assert.True(session.Execute(new("player"), new ConstructFacilityCommand(Guid.NewGuid(), 1, 0, 1, [11], 10000, 1,
            8000, 9000, new(17, 2))).Submission.Accepted);
        session.AdvanceXun();
        var building = session.View.Settlements.Single(s => s.Id == 8000);
        Assert.True(building.IsUnderConstruction);
        Assert.Equal(9000, building.FacilityTypeId);
        Assert.Equal(3, building.X);
        Assert.Empty(ConstructionLocations.Read(session, 1, 9000));
        var restored = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Snapshot.StateHash, restored.Snapshot.StateHash);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        }
        Assert.False(restored.View.Settlements.Single(s => s.Id == 8000).IsUnderConstruction);
        Assert.Equal(10000u, restored.View.Settlements.Single(s => s.Id == 8000).Soldiers);
        Assert.Equal(8000, restored.View.Officers.Single().SettlementId);
    }
}
