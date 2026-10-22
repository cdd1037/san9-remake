using San9.Tests;
using System.Text;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AllianceFormationSessionTests
{
    [Theory]
    [InlineData(5UL, true)]
    [InlineData(0UL, false)]
    public void AlliancePresentationStopsHistoricalTableWhileFailedQualificationContinues(ulong seed, bool offered)
    {
        var definition = Definition() with
        {
            Month = 3, Day = 21,
            Officers = [new("ruler", new(11, 5, 500, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
                new("companion", new(12, 5, 500, new(50, 50, 50, 50), false)),
                new("rival", new(21, 1, 100, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
                new("participant", new(31, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
            QiaoMarriage = new(11, 12, [31], 21, 1, [100, 200, 300, 400], new(199, 12, 30), new(209, 12, 30), 3, 7, 20, 25, new("qiao-marriage")),
            AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, CityIndependenceEnabled = false,
            FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, OfficeRequestsEnabled = false
        };
        definition = definition with { Forces = definition.Forces.Select(f => f with { LeaderId = f.Id switch { 5 => 11, 1 => 21, _ => null } }).ToArray() };
        definition = definition with { Information = definition.Information with { Corps = definition.Information.Corps.Select(c => c with { LeaderId = definition.Forces.Single(f => f.Id == c.OwnerId).LeaderId }).ToArray() } };
        var initial = GameSession.Start(definition, seed).Capture();
        var session = GameSession.Restore(definition, initial with { World = initial.World with
        { Clock = new(191, 3, 21) } });
        var arrival = session.AdvanceXun();
        Assert.Equal(offered, arrival.Events.OfType<AllianceProposedEvent>().Any());
        if (offered)
        {
            Assert.Empty(arrival.Events.OfType<QiaoMarriageEvent>());
            while (session.GetAllianceParticipationPrompt() is { } prompt)
            {
                int player = prompt.Proposal.RespondingForceId!.Value;
                var seat = session.Snapshot.Simulation.Controls.Single(c => c.ForceId == player);
                var answer = session.Execute(new(seat.ControllerId), new AllianceParticipationCommand(Guid.NewGuid(), player,
                    session.Snapshot.Simulation.NextSequence, prompt.Proposal.TargetForceId, false));
                Assert.True(answer.Submission.Accepted);
                Assert.Empty(answer.Events.OfType<QiaoMarriageEvent>());
            }
            Assert.NotNull(session.Snapshot.Simulation.Alliance);
            Assert.DoesNotContain(new PersistentEventKey("qiao-marriage"), session.Snapshot.Simulation.ConsumedEventKeys);
        }
        else Assert.Single(arrival.Events.OfType<QiaoMarriageEvent>());
    }

    private static ScenarioInitialState Definition() => new(ScenarioInitialState.CurrentSchemaVersion, "alliance-choice", 190, 1, 1, 5,
        Enumerable.Range(1, 5).Select(id => new ForceInitialState(id, $"force.{id}", id, 50) { BaseTrust = 50 }).ToArray(),
        Enumerable.Range(1, 5).Select(id => new SettlementInitialState(id * 100, $"city.{id}", id, id, 0, 0, 100, 100, 0, [])
            { Population = 100000, DraftPopulation = id == 1 ? 400001u : 0, PublicOrder = 500 }).ToArray(), [], [])
    {
        CityProvinceIds = Enumerable.Range(1, 5).ToDictionary(id => id * 100, _ => 1),
        Information = InformationDefinitions.Empty with { Corps = [.. Enumerable.Range(1, 5).Select(id => new San9.Core.Domain.CorpsState(id, id, null, new(10000, 100000)) { IsRoot = true })], Relations = [new(1, 5, 80)] }
    };

    private static GameSession Ready(ScenarioInitialState definition)
    {
        var initial = GameSession.Start(definition, 5).Capture();
        return GameSession.Restore(definition, initial with { World = initial.World with { Clock = new(190, 12, 21) } });
    }

    [Fact]
    public void ChoiceSuspendsQuarterInMemoryAndReplaysFromStableSave()
    {
        var definition = Definition();
        var session = Ready(definition);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        restored.AdvanceXun();
        uint draftPopulation = session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation;
        session.AdvanceXun();
        Assert.Equal(draftPopulation, session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation);
        Assert.Equal(5, session.GetAllianceParticipationPrompt()!.Proposal.RespondingForceId);
        var pendingHash = session.Snapshot;
        Assert.Equal(SessionRejection.NotStable, session.Apply(session.Request(new AdvanceOperation())).Rejection);
        StateAssert.Equal(pendingHash, session.Snapshot);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        StateAssert.Equal(pendingHash, restored.Snapshot);
        var command = new AllianceParticipationCommand(Guid.NewGuid(), 5, session.Snapshot.Simulation.NextSequence, 1, true);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.True(restored.Execute(new("player"), command).Submission.Accepted);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(new[] { 2, 3, 4, 5 }, session.Snapshot.Simulation.Alliance!.MemberForceIds);
        Assert.Equal(75, session.Snapshot.Forces.Single(f => f.Id == 5).BaseTrust);
        Assert.Equal(draftPopulation + 100, session.Snapshot.Settlements.Single(s => s.Id == 100).DraftPopulation);
        Assert.True(session.Snapshot.Relations.Count > definition.Information.Relations.Length);
        var final = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), final.Capture());
    }

    [Fact]
    public void PendingSaveAndImplicitJoinValueAreRejected()
    {
        var definition = Definition();
        var session = Ready(definition);
        session.AdvanceXun();
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var command = JsonNode.Parse(CanonicalJson.Text<GameCommand>(new AllianceParticipationCommand(Guid.NewGuid(), 5, 0, 1, false)))!;
        command.AsObject().Remove("join");
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<GameCommand>(command.ToJsonString(),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, AllowOutOfOrderMetadataProperties = true }));
    }
}
