using San9.Application.AI;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AiProductionBaselineTests
{
    [Fact]
    public void ChangingControllerOwnershipPreservesPublicCommandRulesAndSavedConfiguration()
    {
        var content = G01ScenarioTests.Fixture();
        var player = GameSession.Start(content, 190);
        var automatic = GameSession.Start(content, 190, aiPolicy: AiPolicyConfiguration.Standard with { WorkBudget = 16 },
            playerForceController: ControllerKind.Automatic);
        var command = new DevelopCommand(Guid.NewGuid(), content.PlayerForceId, 0,
            new(content.PlayerForceId, 1, [2], DomesticActivity.Commerce));
        var a = player.Execute(new("player"), command);
        var b = automatic.Execute(new("player"), command);
        Assert.True(a.Submission.Accepted);
        Assert.Equal(a.Submission, b.Submission);
        Assert.Equal(CanonicalJson.Text(a.Events), CanonicalJson.Text(b.Events));
        Assert.Equal(player.Snapshot.Corps, automatic.Snapshot.Corps);
        Assert.Equal(player.Snapshot.Settlements, automatic.Snapshot.Settlements);
        var restored = SaveService.Decode(SaveService.Encode(automatic), content);
        Assert.Equal(automatic.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(ControllerKind.Automatic, restored.PlayerForceController);
        Assert.Equal(16, restored.AiState.Configuration.WorkBudget);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content,
            automatic.Capture() with { PlayerForceController = ControllerKind.Player }));
    }
}
