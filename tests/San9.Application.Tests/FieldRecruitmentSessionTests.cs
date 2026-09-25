using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FieldRecruitmentSessionTests
{
    private static OfficerState Person(int id, int owner, int? home) =>
        new OfficerState(id, owner, home, new(id == 20 ? 100 : 50, 50, 100, 50), false)
        { Status = id is 1 or 2 ? OfficerStatus.Ruler : OfficerStatus.Serving, UnitId = home is null ? 100 : null }
        .WithLoyalty(owner, id == 21 ? 100 : 0).WithLoyalty(1, 255);

    internal static ScenarioInitialState Definition(bool success) => new(ScenarioInitialState.CurrentSchemaVersion, "field-recruitment", 190, 1, 1, 1,
        [new(1, "force.1", 1, 1000) { LeaderId = 1, BaseTrust = 1000 },
         new(2, "force.2", 2, 0) { LeaderId = 2 }],
        [new(1, "home", 1, 0, 0, 1000, 100, 100, 70, []) { Kind = SettlementKind.City, CorpsId = 1, ConsumesFood = false },
         new(2, "enemy", 2, 180, 0, 1000, 100, 100, 70, []) { Kind = SettlementKind.City, CorpsId = 2, ConsumesFood = false }],
        new() { [1] = "home", [2] = "enemy" }, new() { [1] = 1, [2] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
        ForceConsolidationEnabled = false, SettlementDefectionsEnabled = false,
        Information = new([], [], [], [new(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new(2, 2, 2, new(10000, 10000)) { IsRoot = true }], []) { Relations = [new(1, 2, 50)] },
        Officers = [new("ruler.1", Person(1, 1, 1)), new("ruler.2", Person(2, 2, 2)), new("actor", Person(11, 1, 1)),
            new("commander", Person(20, 2, null).WithLoyalty(1, success ? 255 : 0)), new("loyal", Person(21, 2, null)),
            new("follower", Person(22, 2, null))],
        Units = [new(100, 2, new(332, 2), 5000) { FormationId = 1, CommanderId = 20, ServicePlaceId = 2, CorpsId = 2, Morale = 70 }],
        Military = new(200, 2, Enumerable.Repeat(0, 400).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(902, 2), new(1, 1))])
            { RegionIds = Enumerable.Repeat<int?>(2, 400).ToImmutableArray() }
    };

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PublicFieldRecruitmentPreservesBothJourneyLegsAndRecording(bool success)
    {
        var definition = Definition(success); var session = GameSession.Start(definition, 17);
        session.BeginRecording();
        var command = new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var submitted = Assert.Single(session.Snapshot.Simulation.Tasks);
        Assert.Equal(100, submitted.Journey!.RecruitmentUnitId);
        Assert.True(submitted.Journey.Route.FieldDestination);
        Assert.Equal(new WorldPosition(332, 2), submitted.Journey.Route.Destination);
        session.AdvanceXun();
        Assert.Contains(session.Snapshot.Simulation.Tasks, t => t.Kind == "personnel-recruit");
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Equal(success, Assert.Single(result.Events.OfType<RecruitmentResolvedEvent>()).Success);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(success ? 1 : 2, session.Snapshot.Units.Single(u => u.Id == 100).OwnerId);
        Assert.Equal(2, session.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.Equal(success, result.Events.OfType<UnitTransferredEvent>().Any());
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 3; i++)
            Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 11);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void FieldTargetIdentityIsRequiredAndMalformedPayloadCannotRestore()
    {
        var definition = Definition(true); var session = GameSession.Start(definition, 17);
        Assert.True(session.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20)).Submission.Accepted);
        var task = Assert.Single(session.Snapshot.Simulation.Tasks);
        var json = JsonNode.Parse(CanonicalJson.Text(task.Journey))!.AsObject();
        Assert.True(json.Remove("recruitmentUnitId"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PersonnelJourney>(json.ToJsonString(), CanonicalJson.Options));
        var capture = session.Capture();
        var malformed = capture with { World = capture.World with { Simulation = capture.World.Simulation with
            { Tasks = [task with { Journey = task.Journey! with { RecruitmentUnitId = null } }] } } };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, malformed));
    }
}
