using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceEquipmentEditorTests
{
    private static SimulationEngine Engine()
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new ForceState(1, 10) { LeaderId = 1, BaseTrust = 10 }, new ForceState(2, 20) { LeaderId = 2, BaseTrust = 20 }],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new SettlementState(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            relations: [new(1, 2, 50)], controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }, new CorpsState(2, 2, 2, new(0, 0)) { IsRoot = true }]);
        var rules = new SimulationRules { Equipment = new EquipmentRulebook([
            new("sword", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default),
            new("blade", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default),
            new EquipmentDefinition("axe", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default) { MinimumAbilities = new(60, 0, 0, 0) },
            new("cap", EquipmentSlot.Helmet, EquipmentRarity.Ordinary, default, default)]) };
        var snapshot = new SimulationEngine(world, rules).CaptureStableState() with { EquipmentInitialized = true, Equipment = [
            new EquipmentInstance(1, "sword", 1, null, true), new EquipmentInstance((long)int.MaxValue + 3, "cap", null, 1, false),
            new EquipmentInstance(3, "axe", null, 1, false), new EquipmentInstance(4, "blade", 1, null, false)] };
        return new SimulationEngine(WorldState.Restore(snapshot), rules);
    }

    [Fact]
    public void ForceEditChangesBaseColorAndRelationshipWithoutDiplomaticEvents()
    {
        var engine = Engine();
        int oldTrust = engine.Snapshot.Forces.Single(f => f.Id == 1).Trust;
        var events = engine.EditWorld(WorldEditorKind.Forces, [new ForceEdit(1, 20, 4, [new(1, 2, 80)])]);
        var force = engine.Snapshot.Forces.Single(f => f.Id == 1);
        Assert.Equal(20, force.BaseTrust); Assert.Equal(4, force.ColorCode);
        Assert.Equal(oldTrust + 10, force.Trust);
        Assert.Equal(80, engine.Snapshot.Relations.Single().Value);
        Assert.DoesNotContain(events, e => e is ForceRelationChangedEvent);
    }

    [Fact]
    public void EquippingReplacesSameSlotWithoutTransferringTheOldItem()
    {
        var engine = Engine();
        engine.EditEquipment([new(4, 1, null, true, 1, 0)]);
        Assert.False(engine.Snapshot.Equipment.Single(e => e.Id == 1).Equipped);
        Assert.Equal(1, engine.Snapshot.Equipment.Single(e => e.Id == 1).OfficerId);
        Assert.True(engine.Snapshot.Equipment.Single(e => e.Id == 4).Equipped);
    }

    [Fact]
    public void LongEquipmentIdsAndAtomicFailuresPreserveExistingEquipment()
    {
        var engine = Engine(); long id = (long)int.MaxValue + 3;
        engine.EditEquipment([new(id, 1, null, true, 2, 25)]);
        var item = engine.Snapshot.Equipment.Single(e => e.Id == id);
        Assert.True(item.Equipped); Assert.Equal(2, item.Level); Assert.Equal(25, item.Experience);
        var before = engine.Snapshot;
        Assert.Throws<ArgumentException>(() => engine.EditEquipment([new(id, 2, null, true, 2, 25), new(3, 1, null, true, 1, 0)]));
        Assert.Same(before, engine.Snapshot);
    }

    [Fact]
    public void HiddenTreasureCanMoveToInventoryAndThenBeEquipped()
    {
        var source = Engine().CaptureStableState() with
        {
            Treasures = [new TreasureState(7, null, TreasureDiscovery.Buried, 1)],
            Equipment = [new EquipmentInstance(4, "named", null, null, false) { LegacyTreasureId = 7 }]
        };
        var items = new EquipmentRulebook([new EquipmentDefinition("named", EquipmentSlot.MainHand, EquipmentRarity.Rare, default, default) { LegacyTreasureId = 7 }]);
        var rules = new SimulationRules { Equipment = items, Treasures = new TreasureRulebook([new(7, default, 0)], items) };
        var engine = new SimulationEngine(WorldState.Restore(source), rules);
        engine.EditEquipment([new(4, null, 1, false, 1, 0)]);
        Assert.Equal(TreasureDiscovery.Discovered, engine.Snapshot.Treasures.Single().Discovery);
        Assert.Equal(1, engine.Snapshot.Equipment.Single().InventoryForceId);
        var command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 4);
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(1, engine.Snapshot.Treasures.Single().HolderId);
        Assert.True(engine.Snapshot.Equipment.Single().Equipped);
        _ = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
    }

    [Fact]
    public void EditingAnUnclaimedGiftClearsItsEventIdentityOnlyWhenMoved()
    {
        var source = Engine().CaptureStableState() with
        {
            Equipment = [new EquipmentInstance(4, "blade", null, null, false) { IsLost = true, EventGiftKey = "gift.1" }]
        };
        var gift = new EquipmentGiftSearchEventDefinition(1, 4, "blade", new("gift.1"))
            { Id = "gift", Consumption = EventConsumption.OnSuccess, ChanceAbility = CommunityAbility.Politics };
        var rules = new SimulationRules { Equipment = new EquipmentRulebook([
            new("blade", EquipmentSlot.MainHand, EquipmentRarity.Rare, default, default)]),
            Personnel = new PersonnelRulebook([new(1, new(0, 0)), new(2, new(0, 0))], searchEvents: [gift]) };
        var engine = new SimulationEngine(WorldState.Restore(source), rules);
        engine.EditEquipment([new(4, null, null, false, 2, 0)]);
        Assert.Equal("gift.1", Assert.Single(engine.Snapshot.Equipment).EventGiftKey);
        engine.EditEquipment([new(4, null, 1, false, 2, 0)]);
        var moved = Assert.Single(engine.Snapshot.Equipment);
        Assert.Null(moved.EventGiftKey);
        Assert.False(moved.IsLost);
        Assert.Equal(1, moved.InventoryForceId);
    }

    [Fact]
    public void LoadingRejectsUnmatchedEquipmentGiftMarker()
    {
        var source = Engine().CaptureStableState() with
        {
            Equipment = [new EquipmentInstance(4, "blade", null, null, false) { IsLost = true, EventGiftKey = "missing" }]
        };
        var rules = new SimulationRules { Equipment = new EquipmentRulebook([
            new("blade", EquipmentSlot.MainHand, EquipmentRarity.Rare, default, default)]) };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(source), rules));
    }
}
