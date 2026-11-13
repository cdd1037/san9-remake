using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EquipmentOwnershipTests
{
    private static WorldMutation World() => WorldState.Create(new(190, 1, 1), 1,
        [new ForceState(1, 0) { LeaderId = 1 }, new ForceState(2, 0) { LeaderId = 3 }],
        [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new SettlementState(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(2, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Serving },
         new OfficerState(3, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        corps: [new CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new CorpsState(2, 2, 3, new(1000, 1000)) { IsRoot = true }],
        equipment: [new EquipmentInstance(10, "sword", 2, null, true, 2, 50),
            new EquipmentInstance(11, "armor", null, 1, false)]).BeginMutation();

    [Fact]
    public void DeathReturnsOrdinaryEquipmentToForceInventory()
    {
        var world = World(); List<DomainEvent> events = [];
        Assert.True(OfficerDeath.Remove(world, 2, OfficerDeathCause.Natural, PersonnelRulebook.Empty, events, false));
        var item = world.AllEquipment[10];
        Assert.Null(item.OfficerId);
        Assert.Equal(1, item.InventoryForceId);
        Assert.False(item.Equipped);
        Assert.Equal(2, item.Level);
        Assert.Equal(50, item.Experience);
        Assert.Single(events.OfType<EquipmentOwnershipResolvedEvent>());
    }

    [Fact]
    public void ForceInventoryFollowsSuccessorOrBecomesLostWithoutReusingIdentity()
    {
        var world = World(); List<DomainEvent> events = [];
        EquipmentOwnership.ForceRetired(world, 1, 2, events);
        Assert.Equal(2, world.AllEquipment[11].InventoryForceId);
        EquipmentOwnership.ForceRetired(world, 2, null, events);
        Assert.True(world.AllEquipment[11].IsLost);
        Assert.Null(world.AllEquipment[11].InventoryForceId);
        Assert.True(world.AllEquipment[11].IsValid);
        Assert.Equal(2, events.OfType<EquipmentOwnershipResolvedEvent>().Count());
    }

    [Fact]
    public void CapturedOfficerKeepsItemsButLosesEquippedEffects()
    {
        var world = World();
        world.Replace(world.Officers[2] with { Status = OfficerStatus.Captive });
        Assert.Equal(2, world.AllEquipment[10].OfficerId);
        Assert.False(world.AllEquipment[10].Equipped);
    }
}
