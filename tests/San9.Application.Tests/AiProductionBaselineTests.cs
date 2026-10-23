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
    public void AutomaticControlsRunEconomicWorkOnceAndRestoreExactly(
        ControllerKind controller, CorpsControlMode corpsMode)
    {
        var basis = G01ScenarioTests.Fixture();
        var content = basis with { Information = basis.Information with { Corps = basis.Information.Corps
            .Select(c => c with { ControlMode = corpsMode }).ToArray() } };
        var session = GameSession.Start(content, 190, playerForceController: controller);
        var before = session.Capture();
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var invalid = session.Apply(session.Request(new EndControlOperation(), "forged"));
        Assert.Equal(SessionDisposition.Rejected, invalid.Disposition);
        StateAssert.Equal(before, session.Capture());

        foreach (var seat in before.World.Simulation.Controls)
        {
            var expected = restored.Apply(restored.Request(new EndControlOperation(), seat.ControllerId));
            var actual = session.Apply(session.Request(new EndControlOperation(), seat.ControllerId));
            Assert.Equal(SessionDisposition.Accepted, actual.Disposition);
            EventAssert.Equal(expected.Events, actual.Events);
            StateAssert.Equal(restored.Capture(), session.Capture());
            Assert.All(actual.Events.OfType<DevelopmentCompletedEvent>(), e => Assert.Equal(seat.ForceId, e.Command.ForceId));
        }
        Assert.True(session.Snapshot.Simulation.NextSequence > before.World.Simulation.NextSequence);
        Assert.NotEmpty(session.Records);
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
        var command = new DevelopCommand(Guid.NewGuid(), content.PlayerForceId!.Value, 0,
            new(content.PlayerForceId!.Value, 1, [2], DomesticActivity.Commerce));
        var a = player.Execute(new("player"), command);
        var b = automatic.Execute(new("ai:1"), command);
        Assert.True(a.Submission.Accepted);
        Assert.Equal(a.Submission, b.Submission);
        EventAssert.Equal(a.Events, b.Events);
        Assert.Equal(player.Snapshot.Corps, automatic.Snapshot.Corps);
        Assert.Equal(player.Snapshot.Settlements, automatic.Snapshot.Settlements);
        var restored = SaveService.Decode(SaveService.Encode(automatic), content);
        StateAssert.Equal(automatic.Capture(), restored.Capture());
        Assert.Equal(ControllerKind.Automatic, restored.PlayerForceController);
        Assert.Null(restored.View.PlayerForceId);
    }
}
