using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class IndividualEquipmentTests
{
    [Fact]
    public void PreciousEquipmentCannotBeManufacturedOrIssuedAsOrdinary()
    {
        var named = new EquipmentDefinition("named", EquipmentSlot.MainHand, EquipmentRarity.Precious, default, default)
            { ResearchProjectId = "smithing", ProductionCost = 100, ProductionDays = 30 };
        Assert.False(named.IsValid);
        Assert.False((named with { ResearchProjectId = null, ProductionCost = 0, ProductionDays = 0, InitialIssue = true }).IsValid);
    }

    [Fact]
    public void OrdinaryIssueCannotBeEquippedButNamedEquipmentCan()
    {
        var ordinary = new EquipmentDefinition("issue", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)
            { InitialIssue = true };
        var named = new EquipmentDefinition("named", EquipmentSlot.MainHand, EquipmentRarity.Rare, new(2, 0, 0, 0), default);
        var rules = new EquipmentRulebook([ordinary, named]);
        EquipmentInstance[] inventory = [new(1, "issue", 1, null, false), new(2, "named", 1, null, false)];
        Assert.Null(EquipmentRules.Equip(inventory, rules.Items, 1, 1));
        Assert.True(EquipmentRules.Equip(inventory, rules.Items, 1, 2)!.Value.Single(item => item.Id == 2).Equipped);
    }

    [Fact]
    public void PendingMigrationVoucherCannotBeEquipped()
    {
        var ordinary = new EquipmentDefinition("issue", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)
            { InitialIssue = true };
        var rules = new EquipmentRulebook([ordinary]);
        EquipmentInstance[] inventory = [new EquipmentInstance(1, "issue", 1, null, false) { PendingArmamentCredit = 20 }];
        Assert.True(EquipmentRules.ValidInventory(inventory, rules.Items));
        Assert.Null(EquipmentRules.Equip(inventory, rules.Items, 1, 1));
    }
}
