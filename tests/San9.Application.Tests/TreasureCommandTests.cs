using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class TreasureCommandTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "treasure-commands", 190, 1, 1, 1,
        [new(1, "force", 1, 1000) { LeaderId = 1, BaseTrust = 950 }],
        [new(1, "city", 1, 0, 0, 0, 100, 100, 0, [])], new() { [1] = "city" }, new() { [1] = 1 })
    {
        ImperialSealId = 10,
        Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Ruler }),
            new("recipient", new OfficerState(2, 1, 1, new(50, 50, 50, 50), false).WithLoyalty(1, 98))],
        Information = new([], [], [new(10, "treasure", 1, 10, 8, null) { AbilityBonuses = new(10, 0, 0, 0) }], [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }],
            [new(10, 1, TreasureDiscovery.Discovered, null)])
    };

    [Fact]
    public void ImperialSealTrustRequiresEquippingAndTracksTransferAndRestore()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        int Trust() => session.Snapshot.Forces.Single(force => force.Id == 1).Trust;
        Assert.Equal(1000, Trust());
        Assert.True(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, EquipmentSlot.MainHand, null)).Submission.Accepted);
        Assert.Equal(950, Trust());
        Assert.True(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 10)).Submission.Accepted);
        Assert.Equal(1000, Trust());
        Assert.True(session.Execute(new("player"), new GrantTreasureCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 10, 2)).Submission.Accepted);
        Assert.Equal(950, Trust());
        Assert.True(session.Execute(new("player"), new ConfiscateTreasureCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 10)).Submission.Accepted);
        Assert.Equal(950, Trust());
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(950, restored.Snapshot.Forces.Single(force => force.Id == 1).Trust);
        Assert.True(restored.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1,
            restored.Snapshot.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 10)).Submission.Accepted);
        Assert.Equal(1000, restored.Snapshot.Forces.Single(force => force.Id == 1).Trust);
    }

    [Fact]
    public void TransferCommandsKeepQueriesSaveRestoreConsistent()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        int initialLoyalty = session.Snapshot.Officers.Single(p => p.Id == 2).Loyalty;
        var id = Guid.NewGuid();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new CommandOperation(
            new GrantTreasureCommand(id, 1, 0, 10, 2)), id: id)).Disposition);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new[] { "treasure", "recipient", "ruler" }
            .ToDictionary(k => k, k => k));
        Assert.Empty(TreasureCatalog.Read(session.View, texts, holderId: 1).Rows);
        Assert.Equal("未装备", Assert.Single(TreasureCatalog.Read(session.View, texts, holderId: 2).Rows).Cells[7].Text);
        Assert.Equal(50, session.View.Officers.Single(p => p.Id == 2).Abilities.War);
        Assert.True(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1, 1, 2, EquipmentSlot.MainHand, 10)).Submission.Accepted);
        Assert.Equal("已装备", Assert.Single(TreasureCatalog.Read(session.View, texts, holderId: 2).Rows).Cells[7].Text);
        Assert.Equal(60, session.View.Officers.Single(p => p.Id == 2).Abilities.War);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        id = Guid.NewGuid();
        var request = session.Request(new CommandOperation(new ConfiscateTreasureCommand(id, 1, 2, 10)), id: id);
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        EventAssert.Equal(result.Events, restored.Apply(request).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(initialLoyalty + 8 - 12, session.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        Assert.Equal(1, definition.Information.TreasureStates.Single().HolderId);
        Assert.Equal(50, GameSession.Start(definition, 190).View.Officers.Single(p => p.Id == 2).Abilities.War);
    }

    [Fact]
    public void TacticCountsAndMarksFollowGrantConfiscationAndSaveRestore()
    {
        var source = Fixture();
        var definition = source with
        {
            Forces = source.Forces.Select(f => f with { ProvidedTactics = [1] }).ToArray(),
            Officers = source.Officers.Select(p => p with { State = p.State with { LearnedTactics = [1] } }).ToArray(),
            Information = source.Information with { Treasures = [source.Information.Treasures[0] with { TacticId = 0 }] }
        };
        definition = PresentationTacticFixture.WithInfantry(definition);
        var session = GameSession.Start(definition, 190);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new[] { "treasure", "recipient", "ruler" }.ToDictionary(k => k, k => k));

        long Count(StrategySnapshot view, int id) => OfficerCatalog.Read(view, texts, 0, null, null, id).Rows.Single().Cells[5].Number!.Value;
        string[] Marks(StrategySnapshot view, int id) => OfficerCatalog.Read(view, texts, 4, null, null, id).Rows.Single().Cells.Skip(3).Select(c => c.Text).ToArray();
        var before = session.View;
        Assert.Equal(2, Count(before, 1));
        Assert.Equal(1, Count(before, 2));
        Assert.Equal(["△", "○", ""], Marks(before, 1));
        Assert.Equal(["", "○", ""], Marks(before, 2));

        Assert.True(session.Execute(new("player"), new GrantTreasureCommand(Guid.NewGuid(), 1, 0, 10, 2)).Submission.Accepted);
        Assert.Equal(1, Count(session.View, 1));
        Assert.Equal(1, Count(session.View, 2));
        Assert.True(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1, 1, 2, EquipmentSlot.MainHand, 10)).Submission.Accepted);
        Assert.Equal(2, Count(session.View, 2));
        Assert.Equal(["△", "○", ""], Marks(session.View, 2));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(2, Count(restored.View, 2));
        Assert.True(session.Execute(new("player"), new ConfiscateTreasureCommand(Guid.NewGuid(), 1, 2, 10)).Submission.Accepted);
        Assert.Equal(1, Count(session.View, 1));
        Assert.Equal(1, Count(session.View, 2));
        Assert.Equal(["", "○", ""], Marks(session.View, 2));
        Assert.All(session.Snapshot.Officers, p => Assert.Equal([1], p.LearnedTactics.ToArray()));
        Assert.Equal(2, Count(before, 1));
        Assert.Equal(1, Count(before, 2));
    }

    [Fact]
    public void RestoreAndContentDeriveAbilitiesFromChangedHoldings()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var state = session.Capture();
        var changed = state with { World = state.World with
        { Treasures = [state.World.Treasures.Single() with { HolderId = 2 }],
          Equipment = [state.World.Equipment.Single() with { OfficerId = 2, Equipped = false }] } };
        Assert.Equal(50, GameSession.Restore(definition, changed).View.Officers.Single(p => p.Id == 2).Abilities.War);
        var initial = definition with { Information = definition.Information with
        { TreasureStates = [definition.Information.TreasureStates.Single() with { HolderId = 2 }] } };
        initial.Validate();
        Assert.Equal(60, GameSession.Start(initial, 190).View.Officers.Single(p => p.Id == 2).Abilities.War);
    }
}
