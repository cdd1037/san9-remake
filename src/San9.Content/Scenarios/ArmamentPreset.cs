using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Initial balancing values for the built-in troop catalog.</summary>
public static class ArmamentPreset
{
    public static ImmutableArray<ArmamentDefinition> Basic() =>
    [
        new("iron-spear", "铁矛", ArmamentSlot.Weapon, [0, 4], 8, 0, 100, null) { ProductionCostPerThousand = 50, ProductionDays = 30 },
        new("iron-halberd", "铁戟", ArmamentSlot.Weapon, [0, 4], 12, 0, 160, "spear-2") { ProductionCostPerThousand = 80, ProductionDays = 30 },
        new("ring-pommel-saber", "环首刀", ArmamentSlot.Weapon, [1, 4], 8, 0, 100, null) { ProductionCostPerThousand = 50, ProductionDays = 30 },
        new("bow", "弓", ArmamentSlot.Weapon, [2, 5], 8, 0, 100, null) { ProductionCostPerThousand = 50, ProductionDays = 30 },
        new("crossbow", "弩", ArmamentSlot.Weapon, [3], 12, 0, 160, "crossbow-1") { ProductionCostPerThousand = 80, ProductionDays = 30 },
        new("leather-armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, null) { ProductionCostPerThousand = 50, ProductionDays = 30 },
        new("lamellar-armor", "札甲", ArmamentSlot.Armor, [], 0, 12, 160, "shield-2") { ProductionCostPerThousand = 80, ProductionDays = 30 }
    ];
}
