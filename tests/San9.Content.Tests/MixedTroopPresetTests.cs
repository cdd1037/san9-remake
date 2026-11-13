using San9.Content.Scenarios;
using San9.Core.Domain;
using System.Collections.Immutable;

namespace San9.Content.Tests;

public sealed class MixedTroopPresetTests
{
    [Fact]
    public void ThirdTierTacticsRequireThirdTierTroopsAndResearch()
    {
        var types = MixedTroopPreset.TroopTypes([0, 1, 2], [3, 4, 5], [6, 7, 8], [9, 10, 11]);
        var projects = MixedTroopPreset.TroopResearchProjects().ToDictionary(project => project.Id);
        Assert.All(types, type =>
        {
            Assert.Equal(3, type.MaximumLevel);
            string third = type.LevelResearchProjects[3];
            Assert.Contains(type.LevelResearchProjects[2], projects[third].Prerequisites);
            Assert.True(type.EquipmentCostPerThousand.ContainsKey(3));
        });
        Assert.Equal(3, types[0].TacticMinimumLevels[2]);
        Assert.Equal(3, types[1].TacticMinimumLevels[2]);
        Assert.DoesNotContain(11, types[2].TacticMinimumLevels.Keys);
        Assert.Equal(2, types[3].TacticMinimumLevels[10]);
        Assert.Equal(3, types[3].TacticMinimumLevels[11]);
        Assert.Equal(3, types[4].TacticMinimumLevels[5]);
        Assert.Equal(3, types[5].TacticMinimumLevels[8]);
    }

    [Fact]
    public void LegacyPresetUpgradesWithoutRewritingCustomTroops()
    {
        var standard = MixedTroopPreset.TroopTypes([0, 1, 2], [3, 4, 5], [6, 7, 8], [9, 10, 11]);
        var old = standard.Select(type => type with
        {
            MaximumLevel = 2,
            TacticMinimumLevels = type.TacticMinimumLevels.ToImmutableDictionary(pair => pair.Key,
                pair => type.Id == 3 && pair.Key == 10 ? 1 : Math.Min(pair.Value, 2)),
            LevelResearchProjects = type.LevelResearchProjects.Remove(3),
            EquipmentCostPerThousand = type.EquipmentCostPerThousand.Remove(3)
        }).ToImmutableArray();
        var definition = new MilitaryDefinition(1, 1, [0], [], [], [])
        { TroopTypes = old, ResearchProjects = MixedTroopPreset.TroopResearchProjects().Where(p => !p.Id.EndsWith("-3", StringComparison.Ordinal)).ToImmutableArray() };
        var upgraded = MixedTroopPreset.UpgradeLegacyTroopLevels(definition);
        Assert.Equal(3, upgraded.TroopTypes[0].MaximumLevel);
        Assert.Contains(upgraded.ResearchProjects, p => p.Id == "crossbow-3");
        var custom = definition with { TroopTypes = old.SetItem(0, old[0] with { Name = "自定义枪兵" }) };
        Assert.Same(custom, MixedTroopPreset.UpgradeLegacyTroopLevels(custom));
    }
}
