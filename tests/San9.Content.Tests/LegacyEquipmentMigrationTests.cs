using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class LegacyEquipmentMigrationTests
{
    private static TreasureDefinition Item(int id, int category, int bonus) => new(id, $"item-{id}", category, bonus, 0, null)
    { AbilityBonuses = new(bonus, 0, 0, 0) };

    [Fact]
    public void PreservesHoldingsAndSelectsStrongestInEachSlotWithStableTieBreak()
    {
        TreasureDefinition[] definitions = [Item(4, 1, 2), Item(3, 1, 2), Item(5, 0, 0) with { EscapeMount = true }, Item(6, 5, 1)];
        TreasureState[] states = [new(4, 10, TreasureDiscovery.Discovered, null), new(3, 10, TreasureDiscovery.Discovered, null),
            new(5, 10, TreasureDiscovery.Discovered, null), new(6, 10, TreasureDiscovery.Buried, 1)];
        var result = LegacyEquipmentMigration.Convert(definitions, states);
        var rulebook = new EquipmentRulebook(result.Definitions);
        Assert.True(EquipmentRules.ValidInventory(result.Instances, rulebook.Items));
        Assert.Equal(new long[] { 3, 4, 5, 6 }, result.Instances.Select(i => i.Id));
        Assert.True(result.Instances.Single(i => i.Id == 3).Equipped);
        Assert.False(result.Instances.Single(i => i.Id == 4).Equipped);
        Assert.True(result.Instances.Single(i => i.Id == 5).Equipped);
        Assert.Equal(EquipmentSlot.Mount, result.Definitions.Single(i => i.LegacyTreasureId == 5).Slot);
        Assert.True(result.Definitions.Single(i => i.LegacyTreasureId == 5).EscapeMount);
        Assert.Equal(EquipmentSlot.Book, result.Definitions.Single(i => i.LegacyTreasureId == 6).Slot);
        Assert.Empty(result.UnclassifiedTreasureIds);
    }

    [Fact]
    public void ReportsUnknownCategoriesAndRejectsUnknownActiveReferences()
    {
        var result = LegacyEquipmentMigration.Convert([Item(2, 19, 1)], [new(2, 10, TreasureDiscovery.Discovered, null)]);
        Assert.Equal(2, Assert.Single(result.UnclassifiedTreasureIds));
        Assert.Equal(EquipmentSlot.Treasure, Assert.Single(result.Definitions).Slot);
        Assert.Throws<ArgumentException>(() => LegacyEquipmentMigration.Convert([], [new(1, 10, TreasureDiscovery.Discovered, null)]));
    }

    [Fact]
    public void OldGeneratedBookDefinitionsMoveToBookSlotWithoutChangingIdentity()
    {
        var treasure = Item(8, 2, 3);
        var oldDefinition = new EquipmentDefinition("legacy-treasure:8", EquipmentSlot.Treasure,
            EquipmentRarity.Precious, treasure.AbilityBonuses, default) { LegacyTreasureId = 8 };
        var result = LegacyEquipmentMigration.Convert([treasure], [new(8, 10, TreasureDiscovery.Discovered, null)], [oldDefinition]);
        Assert.Equal(EquipmentSlot.Book, Assert.Single(result.Definitions).Slot);
        Assert.Equal("legacy-treasure:8", Assert.Single(result.Instances).DefinitionId);
        Assert.True(result.Instances[0].Equipped);
    }

    [Fact]
    public void RejectsConflictingAuthoredLegacyLoadout()
    {
        TreasureDefinition[] treasures = [Item(1, 1, 3), Item(2, 11, 2)];
        TreasureState[] states = [new(1, 10, TreasureDiscovery.Discovered, null), new(2, 10, TreasureDiscovery.Discovered, null)];
        EquipmentDefinition[] authored =
        [
            new("main", EquipmentSlot.MainHand, EquipmentRarity.Precious, default, default)
                { LegacyTreasureId = 1, CompatibleWeapons = ["other"] },
            new("off", EquipmentSlot.OffHand, EquipmentRarity.Precious, default, default) { LegacyTreasureId = 2 },
            new("other", EquipmentSlot.OffHand, EquipmentRarity.Ordinary, default, default)
        ];
        Assert.Throws<ArgumentException>(() => LegacyEquipmentMigration.Convert(treasures, states, authored));
    }
}
