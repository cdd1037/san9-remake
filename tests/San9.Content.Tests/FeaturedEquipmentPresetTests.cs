using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class FeaturedEquipmentPresetTests
{
    [Fact]
    public void FeaturedItemsHaveIndependentIdsActualBonusesAndNoOrdinaryProduction()
    {
        var definitions = FeaturedEquipmentPreset.Definitions();
        var rules = new EquipmentRulebook(definitions);
        Assert.Equal(40, rules.Items.Count);
        Assert.Equal(40, definitions.Select(item => item.Name).Distinct().Count());
        Assert.Equal(12, definitions.Count(item => item.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand));
        Assert.Equal(5, definitions.Count(item => item.Slot is EquipmentSlot.Helmet or EquipmentSlot.Armor));
        Assert.Equal(6, definitions.Count(item => item.Slot == EquipmentSlot.Mount));
        Assert.All(definitions, item =>
        {
            Assert.Equal(EquipmentOrigin.Project, item.Origin);
            Assert.NotEqual(default, item.BaseBonuses);
            Assert.Null(item.PurchaseCost);
            Assert.Null(item.ResearchProjectId);
            Assert.False(item.IsOrdinaryArmament);
        });
    }

    [Fact]
    public void PlacementPreservesExistingInstancesAndUsesStableActiveCities()
    {
        SettlementInitialState City(int id, bool dormant = false) => new(id, $"city.{id}", null, 0, 0, 0, 100, 100, 50, [])
            { Kind = SettlementKind.City, IsDormant = dormant };
        var initial = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "test", 190, 1, 1, null,
            [], [City(7), City(3), City(1, true)], [], [])
        {
            Information = InformationDefinitions.Empty with { TreasureStates = [new(10, null, TreasureDiscovery.Buried, 3)] }
        };
        var a = FeaturedEquipmentPreset.Apply(initial);
        var b = FeaturedEquipmentPreset.Apply(initial with { Settlements = initial.Settlements.Reverse().ToArray() });
        Assert.Equal(a.InitialEquipment, b.InitialEquipment);
        Assert.Equal(40, a.InitialEquipment!.Length);
        Assert.Equal(11, a.InitialEquipment[0].Id);
        Assert.Equal(4, a.SearchEvents.OfType<EquipmentGiftSearchEventDefinition>().Count());
        Assert.All(a.InitialEquipment.Where(item => item.SearchPlaceId is not null), item =>
        {
            Assert.True(item.IsLost);
            Assert.Contains(item.SearchPlaceId, new int?[] { 3, 7 });
        });
        Assert.Throws<InvalidDataException>(() => FeaturedEquipmentPreset.Apply(a));
    }
}
