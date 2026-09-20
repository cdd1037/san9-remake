using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DormantScenarioTests
{
    [Fact]
    public void NewGameRetainsDormantContentWithoutControlOrDisplayAndRestorePreservesIt()
    {
        var initial = G01ScenarioTests.Fixture();
        var definition = initial with
        {
            Forces = [.. initial.Forces, initial.Forces[1] with { Id = 3, LeaderId = 7, IsDormant = true }],
            Settlements = [.. initial.Settlements, initial.Settlements[1] with { Id = 3, OwnerId = 3, IsDormant = true }],
            Officers = [.. initial.Officers, initial.Officers[0] with
                { State = new OfficerState(7, 3, 3, new(60, 70, 80, 90), false) { IsDormant = true } }]
        };
        var session = GameSession.Start(definition, 17);
        Assert.Equal(definition.Forces[2].Money, session.Snapshot.Forces.Single(f => f.Id == 3).Money);
        Assert.Equal(definition.Settlements[2].Soldiers, session.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(definition.Officers[^1].State),
            System.Text.Json.JsonSerializer.Serialize(session.Snapshot.Officers.Single(p => p.Id == 7)));
        Assert.DoesNotContain(session.Snapshot.Simulation.Controls, c => c.ForceId == 3);
        Assert.DoesNotContain(session.View.Forces, f => f.Id == 3);
        Assert.DoesNotContain(session.View.Settlements, s => s.Id == 3);
        Assert.DoesNotContain(session.View.Officers, p => p.Id == 7);
        var restored = GameSession.Restore(definition, session.Capture());
        Assert.Equal(session.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.DoesNotContain(restored.View.Forces, f => f.Id == 3);
        Assert.True(restored.Snapshot.Forces.Single(f => f.Id == 3).IsDormant);
    }
}
