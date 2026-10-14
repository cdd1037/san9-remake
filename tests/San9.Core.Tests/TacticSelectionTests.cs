using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class TacticSelectionTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine() => new(WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0)], [new(1, 1, 1000, 100, 100, 70, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(70, 70, 70, 70), true) { LearnedTactics = [8000, 8001, 8003], TacticCooldown = 13 }],
        controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]), EconomyRulebook.Empty, PersonnelRulebook.Empty,
        TreasureRulebook.Empty, military: new(new(1, 1, [0], [new(0, 1, null)], [], [new(1, new(2, 2), new(1, 1))])
        {
            Tactics = [new(8000, 0, TacticEffect.Martial, 13, 9, 100, true, false, []),
                new(8001, 4, TacticEffect.FormationTechnology, 0, 0, 100, false, false, []),
                new(8002, 0, TacticEffect.Martial, 19, 9, 100, true, false, []),
                new(8003, 6, TacticEffect.CounterConfusion, 0, 0, 0, true, false, [])]
        }));

    [Fact]
    public void LearnedCounterTacticCanBeEquippedWithoutClearingCooldown()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, 0, 1, 8003)).Submission.Accepted);
        Assert.Equal(8003, engine.Snapshot.Officers[0].EquippedTacticId);
        Assert.Equal(13, engine.Snapshot.Officers[0].TacticCooldown);
    }

    [Fact]
    public void SelectionIsIndependentOfDomesticActionAndNeverResetsCooldown()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, 0, 1, 8000)).Submission.Accepted);
        Assert.Equal(8000, engine.Snapshot.Officers[0].EquippedTacticId);
        Assert.Equal(13, engine.Snapshot.Officers[0].TacticCooldown);
        Assert.True(engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, 1, 1, null)).Submission.Accepted);
        Assert.Null(engine.Snapshot.Officers[0].EquippedTacticId);
        Assert.Equal(13, engine.Snapshot.Officers[0].TacticCooldown);
        engine.EndControl(Player);
        engine.AdvanceXun();
        Assert.Equal(3, engine.Snapshot.Officers[0].TacticCooldown);
        engine.EndControl(Player);
        engine.AdvanceXun();
        Assert.Equal(0, engine.Snapshot.Officers[0].TacticCooldown);
    }

    [Theory]
    [InlineData(8001)]
    [InlineData(8002)]
    [InlineData(9999)]
    public void PassiveUnknownAndUnownedTacticsRejectAtomically(int tactic)
    {
        var engine = Engine();
        var before = engine.Snapshot;
        Assert.Equal(CommandRejection.TacticUnavailable,
            engine.Execute(Player, new SelectTacticCommand(Guid.NewGuid(), 1, 0, 1, tactic)).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Fact]
    public void CommandAndOfficerStateSerializeSelectedTacticAndCooldown()
    {
        GameCommand command = new SelectTacticCommand(Guid.NewGuid(), 1, 0, 1, 8000);
        Assert.Equal(command, System.Text.Json.JsonSerializer.Deserialize<GameCommand>(System.Text.Json.JsonSerializer.Serialize(command)));
        var engine = Engine();
        engine.Execute(Player, command);
        var officer = engine.Snapshot.Officers[0];
        var restored = System.Text.Json.JsonSerializer.Deserialize<OfficerState>(System.Text.Json.JsonSerializer.Serialize(officer))!;
        Assert.Equal(8000, restored.EquippedTacticId);
        Assert.Equal(13, restored.TacticCooldown);
        Assert.False((officer with { TacticCooldown = -1 }).IsValid());
    }
}
