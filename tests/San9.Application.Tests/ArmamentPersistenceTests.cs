using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ArmamentPersistenceTests
{
    [Fact]
    public void PurchaseCommandAndEventRoundTripThroughProtocol()
    {
        GameCommand command = new PurchaseArmamentCommand(Guid.NewGuid(), 1, 2, 3, "iron-spear", 2);
        DomainEvent result = new ArmamentPurchasedEvent(new(190, 1, 1), 1, 3, "iron-spear", 2, 200);
        Assert.Equal(command, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(command, StrictJson.Options)));
        Assert.Equal(result, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(result, StrictJson.Options)));
        GameCommand production = new StartArmamentProductionCommand(Guid.NewGuid(), 1, 3, 3, "iron-spear", 1);
        DomainEvent completed = new ArmamentProductionCompletedEvent(new(190, 2, 1), 1, 3, "iron-spear", 1);
        DomainEvent transfer = new ArmamentAssetsTransferredEvent(new(190, 2, 1), 2, 1, false);
        Assert.Equal(production, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(production, StrictJson.Options)));
        Assert.Equal(completed, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(completed, StrictJson.Options)));
        Assert.Equal(transfer, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(transfer, StrictJson.Options)));
        GameCommand resupply = new ResupplyUnitArmamentsCommand(Guid.NewGuid(), 1, 4, 7);
        DomainEvent supplied = new UnitArmamentsResuppliedEvent(new(190, 2, 1), 1, 7,
            [new ArmamentResupplyLine(11, ArmamentSlot.Weapon, "iron-spear", 500)]);
        Assert.Equal(resupply, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(resupply, StrictJson.Options)));
        var restoredSupply = Assert.IsType<UnitArmamentsResuppliedEvent>(StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(supplied, StrictJson.Options)));
        Assert.Equal(Assert.IsType<UnitArmamentsResuppliedEvent>(supplied).Lines.ToArray(), restoredSupply.Lines.ToArray());
        GameCommand configure = new ConfigureUnitArmamentCommand(Guid.NewGuid(), 1, 5, 7, 11, ArmamentSlot.Weapon, "iron-spear");
        DomainEvent changed = new UnitArmamentConfiguredEvent(new(190, 2, 1), 1, 7, 11, ArmamentSlot.Weapon, null, 0, "iron-spear", 500);
        Assert.Equal(configure, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(configure, StrictJson.Options)));
        Assert.Equal(changed, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(changed, StrictJson.Options)));
    }

    [Fact]
    public void ForceInventoryAndDetachmentAllocationsRoundTripWhileLegacyDataRemainsReadable()
    {
        var force = new ForceState(1, 0)
        {
            ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("iron-spear", 725),
            ArmamentReserve = ImmutableDictionary<string, ulong>.Empty.Add("iron-spear", (ulong)uint.MaxValue + 123),
            ArmamentMigrationCredit = 240,
            ArmamentEscrowSuccessorId = 2
        };
        var restored = StrictJson.Deserialize<ForceState>(JsonSerializer.SerializeToUtf8Bytes(force, StrictJson.Options));
        Assert.Equal(725u, restored.ArmamentInventory!["iron-spear"]);
        Assert.Equal((ulong)uint.MaxValue + 123, restored.ArmamentReserve!["iron-spear"]);
        Assert.Equal(240, restored.ArmamentMigrationCredit);
        Assert.Equal(2, restored.ArmamentEscrowSuccessorId);
        var oldForce = JsonNode.Parse(JsonSerializer.Serialize(force, StrictJson.Options))!.AsObject();
        oldForce.Remove("armamentInventory");
        oldForce.Remove("armamentReserve");
        oldForce.Remove("armamentMigrationCredit");
        oldForce.Remove("armamentEscrowSuccessorId");
        var legacy = StrictJson.Deserialize<ForceState>(System.Text.Encoding.UTF8.GetBytes(oldForce.ToJsonString()));
        Assert.Null(legacy.ArmamentInventory);
        Assert.Null(legacy.ArmamentReserve);
        Assert.Null(legacy.ArmamentMigrationCredit);
        Assert.Null(legacy.ArmamentEscrowSuccessorId);

        var detachment = new DetachmentState(0, 1, 800)
        {
            Weapon = new("iron-spear", 600, 1000, 200),
            Armor = new("leather-armor", 500, 1000, 200)
        };
        var loaded = StrictJson.Deserialize<DetachmentState>(JsonSerializer.SerializeToUtf8Bytes(detachment, StrictJson.Options));
        Assert.Equal(detachment, loaded);
        var oldDetachment = JsonNode.Parse(JsonSerializer.Serialize(detachment, StrictJson.Options))!.AsObject();
        oldDetachment.Remove("weapon");
        oldDetachment.Remove("armor");
        var oldLoaded = StrictJson.Deserialize<DetachmentState>(System.Text.Encoding.UTF8.GetBytes(oldDetachment.ToJsonString()));
        Assert.Null(oldLoaded.Weapon);
        Assert.Null(oldLoaded.Armor);
    }
}
