using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class EquipmentPresetTests
{
    [Fact]
    public void BasicIssueIsNeutralAffordableAndUsesDistinctCoreSlots()
    {
        var definitions = EquipmentPreset.Basic();
        var issued = definitions.Where(item => item.InitialIssue == true).ToArray();
        Assert.Equal(new[] { "短剑", "纶巾", "皮甲" }, issued.Select(item => item.Name));
        Assert.Equal(new[] { EquipmentSlot.MainHand, EquipmentSlot.Helmet, EquipmentSlot.Armor }, issued.Select(item => item.Slot));
        Assert.All(issued, item =>
        {
            Assert.Equal(EquipmentRarity.Ordinary, item.Rarity);
            Assert.Equal(default, item.BaseBonuses);
            Assert.Equal(default, item.MinimumAbilities);
            Assert.NotNull(item.PurchaseCost);
            Assert.Null(item.PurchaseResearchProjectId);
        });
        _ = new EquipmentRulebook(definitions);
    }

    [Theory]
    [InlineData("长剑")]
    [InlineData("眉尖刀")]
    [InlineData("钩镰刀")]
    [InlineData("双戟")]
    [InlineData("单钩枪")]
    public void OrdinaryHistoricalWeaponFamiliesRequireResearchForAdvancedPurchase(string name)
    {
        var item = Assert.Single(EquipmentPreset.Basic(), definition => definition.Name == name);
        Assert.Equal(EquipmentSlot.MainHand, item.Slot);
        Assert.Equal(2, item.Grade);
        Assert.Equal(EquipmentRarity.Ordinary, item.Rarity);
        Assert.Equal(EquipmentPreset.ResearchId, item.PurchaseResearchProjectId);
    }

    [Fact]
    public void OrdinaryMountsHaveFixedDistinctGradesAndResearchConditions()
    {
        var mounts = EquipmentPreset.Basic().Where(item => item.Slot == EquipmentSlot.Mount).ToArray();
        var yellow = Assert.Single(mounts, item => item.Name == "黄鬃");
        var fine = Assert.Single(mounts, item => item.Name == "良马");
        var swift = Assert.Single(mounts, item => item.Name == "骏马");
        Assert.Equal(new[] { 1, 2, 3 }, new[] { yellow.Grade, fine.Grade, swift.Grade });
        Assert.Null(yellow.PurchaseResearchProjectId);
        Assert.Equal(EquipmentPreset.ResearchId, fine.PurchaseResearchProjectId);
        Assert.Equal(EquipmentPreset.ResearchId, swift.PurchaseResearchProjectId);
        Assert.All(mounts, item => Assert.Equal(default, item.BonusPerLevel));
    }
}
