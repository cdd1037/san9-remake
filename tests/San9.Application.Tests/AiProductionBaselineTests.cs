using San9.Tests;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AiProductionBaselineTests
{
    [Theory]
    [InlineData(ControllerKind.Player, CorpsControlMode.Automatic)]
    [InlineData(ControllerKind.Automatic, CorpsControlMode.Direct)]
    [InlineData(ControllerKind.Automatic, CorpsControlMode.Automatic)]
    public void EndingControlsMatchesPublicEngineWithoutPlanningOrResourceChanges(
        ControllerKind controller, CorpsControlMode corpsMode)
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Information = basis.Information with { Corps = basis.Information.Corps
            .Select(c => c with { ControlMode = corpsMode }).ToArray() } };
        var session = GameSession.Start(content, 190, playerForceController: controller);
        var before = session.Capture();
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(before.World));
        var invalid = session.Apply(session.Request(new EndControlOperation(), "forged"));
        Assert.Equal(SessionDisposition.Rejected, invalid.Disposition);
        StateAssert.Equal(before, session.Capture());

        foreach (var seat in before.World.Simulation.Controls)
        {
            var expected = engine.EndControl(new(seat.ControllerId));
            var actual = session.Apply(session.Request(new EndControlOperation(), seat.ControllerId));
            Assert.Equal(SessionDisposition.Accepted, actual.Disposition);
            EventAssert.Equal(expected.Events, actual.Events);
            StateAssert.Equal(engine.Snapshot, session.Snapshot);
        }
        Assert.Equal(before.World.Simulation.NextSequence, session.Snapshot.Simulation.NextSequence);
        Assert.Empty(session.Records);
    }

    [Fact]
    public void RetiredPolicyStateIsAbsentAndPreviousSaveFormatIsRejected()
    {
        var content = G01ScenarioTests.Fixture();
        var session = GameSession.Start(content, 190);
        var saved = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(session))!;
        Assert.False(saved["session"]!.AsObject().ContainsKey("ai"));
        saved["formatVersion"] = 110;
        var failure = Assert.Throws<SaveException>(() => SaveService.Decode(
            System.Text.Encoding.UTF8.GetBytes(saved.ToJsonString()), content));
        Assert.Equal(SaveFailure.UnsupportedVersion, failure.Failure);
    }

    [Fact]
    public void ChangingControllerOwnershipPreservesPublicCommandRulesAndSavedConfiguration()
    {
        var content = G01ScenarioTests.Fixture();
        var player = GameSession.Start(content, 190);
        var automatic = GameSession.Start(content, 190,
            playerForceController: ControllerKind.Automatic);
        var command = new DevelopCommand(Guid.NewGuid(), content.PlayerForceId, 0,
            new(content.PlayerForceId, 1, [2], DomesticActivity.Commerce));
        var a = player.Execute(new("player"), command);
        var b = automatic.Execute(new("player"), command);
        Assert.True(a.Submission.Accepted);
        Assert.Equal(a.Submission, b.Submission);
        EventAssert.Equal(a.Events, b.Events);
        Assert.Equal(player.Snapshot.Corps, automatic.Snapshot.Corps);
        Assert.Equal(player.Snapshot.Settlements, automatic.Snapshot.Settlements);
        var restored = SaveService.Decode(SaveService.Encode(automatic), content);
        StateAssert.Equal(automatic.Capture(), restored.Capture());
        Assert.Equal(ControllerKind.Automatic, restored.PlayerForceController);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content,
            automatic.Capture() with { PlayerForceController = ControllerKind.Player }));
    }
}
