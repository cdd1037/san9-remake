using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EquipmentCommandTests
{
    private static SimulationEngine Engine()
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new ForceState(1, 1000) { LeaderId = 1 }],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }]);
        var rules = new SimulationRules { Equipment = new EquipmentRulebook([
            new("sword", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default),
            new("axe", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)]) };
        var snapshot = new SimulationEngine(world, rules).CaptureStableState() with
        {
            Equipment = [new EquipmentInstance(1, "sword", 1, null, true), new EquipmentInstance(2, "axe", 1, null, false)]
        };
        return new SimulationEngine(WorldState.Restore(snapshot), rules);
    }

    [Fact]
    public void RequirementsUseBaseAbilitiesAndRejectedEquipDoesNotMoveInventory()
    {
        var source = Engine().CaptureStableState() with { EquipmentInitialized = true, Equipment = [
            new EquipmentInstance(1, "sword", 1, null, true), new EquipmentInstance(2, "axe", null, 1, false)] };
        var catalog = new EquipmentRulebook([
            new EquipmentDefinition("sword", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, new(50, 0, 0, 0), default),
            new EquipmentDefinition("axe", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default) { Grade = 2, MinimumAbilities = new(60, 0, 0, 0) }]);
        var engine = new SimulationEngine(WorldState.Restore(source), new SimulationRules { Equipment = catalog });
        var before = engine.Snapshot;
        var command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 2);
        Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Same(before, engine.Snapshot);
        Assert.Equal(1, engine.Snapshot.Equipment.Single(e => e.Id == 2).InventoryForceId);
        Assert.True(engine.Snapshot.Equipment.Single(e => e.Id == 1).Equipped);
    }

    [Fact]
    public void CommandReplacesAndClearsSlotAndSurvivesRestore()
    {
        var engine = Engine();
        var change = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 2);
        Assert.True(engine.Execute(new("player"), change).Submission.Accepted);
        Assert.False(engine.Snapshot.Equipment.Single(e => e.Id == 1).Equipped);
        Assert.True(engine.Snapshot.Equipment.Single(e => e.Id == 2).Equipped);
        var clear = change with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence, EquipmentId = null };
        Assert.True(engine.Execute(new("player"), clear).Submission.Accepted);
        Assert.All(engine.Snapshot.Equipment, item => Assert.False(item.Equipped));
        Assert.Equal(engine.Snapshot.Equipment, WorldState.Restore(engine.CaptureStableState()).CreateSnapshot().Equipment);
    }

    [Fact]
    public void InvalidSlotOrUnknownItemDoesNotMutateWorld()
    {
        var engine = Engine();
        var before = engine.Snapshot;
        var command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, 1, EquipmentSlot.Helmet, 2);
        Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Same(before, engine.Snapshot);
    }

    [Fact]
    public void FrozenQueriesUseEquippedTacticSources()
    {
        var engine = Engine();
        var definition = new EquipmentDefinition("sword", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default) { TacticId = 8 };
        var catalog = new EquipmentRulebook([definition, new("axe", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)]);
        var treasureRules = new TreasureRulebook([], catalog);
        var current = engine.Snapshot with { EquipmentInitialized = true };
        var frozen = new WorldQuery(current);
        Assert.True(MilitaryTactics.HasTactic(frozen, 1, [1], 8, treasureRules));
        var unequipped = current with { Equipment = current.Equipment.Select(e => e with { Equipped = false }).ToArray() };
        Assert.False(MilitaryTactics.HasTactic(new WorldQuery(unequipped), 1, [1], 8, treasureRules));
    }

    [Fact]
    public void IncompatibleInventoryItemIsNotTransferredOnRejectedEquip()
    {
        var source = Engine().CaptureStableState();
        var catalog = new EquipmentRulebook([
            new EquipmentDefinition("spear", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default) { CompatibleWeapons = ["shield"] },
            new EquipmentDefinition("shield", EquipmentSlot.OffHand, EquipmentRarity.Ordinary, default, default),
            new EquipmentDefinition("bow", EquipmentSlot.OffHand, EquipmentRarity.Ordinary, default, default)]);
        source = source with { EquipmentInitialized = true, Equipment = [new EquipmentInstance(1, "bow", 1, null, true),
            new EquipmentInstance(2, "spear", null, 1, false)] };
        var engine = new SimulationEngine(WorldState.Restore(source), new SimulationRules { Equipment = catalog });
        var before = engine.Snapshot;
        var command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, 1, EquipmentSlot.MainHand, 2);
        Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Same(before, engine.Snapshot);
        Assert.Equal(1, engine.Snapshot.Equipment.Single(e => e.Id == 2).InventoryForceId);
    }
}
