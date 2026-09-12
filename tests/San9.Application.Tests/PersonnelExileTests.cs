using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonnelExileTests
{
    [Fact]
    public void ExileUsesContentGeographyAndPreservesStateAcrossSaveAndReplay()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2" },
            RegionCityIds = new() { [1] = 1, [2] = 2 }, CityConnections = [new(1, 2)]
        };
        var session = GameSession.Start(definition, 190);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        session.BeginRecording();
        Guid id = Guid.NewGuid();
        var request = session.Request(new CommandOperation(new ExileOfficerCommand(id, 1, 0, 2)), id: id);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Apply(request).Events));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var person = session.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(OfficerStatus.Free, person.Status);
        Assert.Null(person.OwnerId);
        Assert.Equal(2, person.SettlementId);
        Assert.Equal(45, person.LoyaltyTo(1));
        restored = SaveService.Decode(SaveService.Encode(restored), definition);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        Assert.NotEqual(GameSession.Fingerprint(definition), GameSession.Fingerprint(definition with { CityConnections = [] }));
    }
}
