using System.Text;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficerAppearanceSessionTests
{
    private static ScenarioInitialState Scenario(bool enabled = true)
    {
        var scenario = G01ScenarioTests.Fixture();
        return scenario with
        {
            Year = 190, Month = 12, Day = 21, AnnualAppearancesEnabled = enabled,
            Officers = scenario.Officers.Append(new OfficerInitialState("child", new OfficerState(9000, null,
                scenario.Officers[0].State.SettlementId, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.NotAppeared, BirthYear = 177, AppearanceYear = 191,
                    ParentId = scenario.Officers[0].State.Id, BloodlineId = 80000 })).ToArray()
        };
    }

    [Fact]
    public void AppearanceBecomesVisibleAndUsableAfterJanuaryAndSaveRecordingRoundTrips()
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 190);
        Assert.DoesNotContain(session.View.Officers, p => p.Id == 9000);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        session.BeginRecording();
        var expected = session.AdvanceXun();
        var actual = restored.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Single(expected.Events.OfType<OfficerAppearedEvent>());
        var visible = Assert.Single(session.View.Officers, p => p.Id == 9000);
        Assert.True(visible.IsServing);
        Assert.Equal("child", visible.NameKey);
        var person = session.Snapshot.Officers.Single(p => p.Id == 9000);
        Assert.False(person.Acted);
        Assert.Equal(80000, person.BloodlineId);
        Assert.Equal(191, person.AppearanceYear);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
        var after = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(after.InitialEvents);
        Assert.Equal(session.Capture().StateHash, after.Capture().StateHash);
        Assert.True(after.Develop(new(person.OwnerId!.Value, person.SettlementId!.Value, [9000], DomesticActivity.Commerce)).Accepted);
    }

    [Fact]
    public void AppearanceInputsAndModeParticipateInContentIdentity()
    {
        var scenario = Scenario();
        string fingerprint = GameSession.Start(scenario, 190).ContentFingerprint;
        Assert.NotEqual(fingerprint, GameSession.Start(Scenario(false), 190).ContentFingerprint);
        foreach (var state in new[] { scenario.Officers[^1].State with { AppearanceYear = 192 },
            scenario.Officers[^1].State with { BloodlineId = null } })
        {
            var changed = scenario with { Officers = scenario.Officers[..^1].Append(scenario.Officers[^1] with { State = state }).ToArray() };
            Assert.NotEqual(fingerprint, GameSession.Start(changed, 190).ContentFingerprint);
        }
    }

    [Theory]
    [InlineData("appearanceYear")]
    [InlineData("bloodlineId")]
    [InlineData("annualAppearancesEnabled")]
    [InlineData("naturalDeathsEnabled")]
    [InlineData("lifespanTier")]
    [InlineData("successionPriority")]
    public void MissingLifecycleSaveInputsAreRejected(string property)
    {
        var scenario = Scenario();
        var session = GameSession.Start(scenario, 190);
        var root = JsonNode.Parse(SaveService.Encode(session))!;
        var world = root["session"]!["world"]!;
        if (property is "annualAppearancesEnabled" or "naturalDeathsEnabled") world["simulation"]!.AsObject().Remove(property);
        else world["officers"]![0]!.AsObject().Remove(property);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            Encoding.UTF8.GetBytes(root.ToJsonString()), scenario, GameSession.EngineFactory(scenario))).Failure);
    }

    [Fact]
    public void DisabledAnnualAppearanceSurvivesSaveAndContinuation()
    {
        var scenario = Scenario(false);
        var session = GameSession.Start(scenario, 190);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.False(restored.Snapshot.Simulation.AnnualAppearancesEnabled);
        Assert.DoesNotContain(restored.AdvanceXun().Events, e => e is OfficerAppearedEvent);
        Assert.DoesNotContain(restored.View.Officers, p => p.Id == 9000);
    }
}
