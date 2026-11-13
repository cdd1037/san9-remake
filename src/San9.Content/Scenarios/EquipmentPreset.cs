using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Original remake equipment and production choices; independent of imported artwork.</summary>
public static class EquipmentPreset
{
    public const string ResearchId = "equipment-forging";

    public static ResearchProjectDefinition Research() => new(ResearchId, 1000, 30, []) { Name = "装备锻造" };

    public static ImmutableArray<EquipmentDefinition> Basic() =>
    [
        New("issue-short-sword", "短剑", EquipmentSlot.MainHand, default, new(1, 0, 0, 0), 100) with { InitialIssue = true },
        New("issue-cloth-cap", "纶巾", EquipmentSlot.Helmet, default, new(0, 0, 0, 1), 80) with { InitialIssue = true },
        New("issue-leather-armor", "皮甲", EquipmentSlot.Armor, default, new(0, 0, 0, 1), 120) with { InitialIssue = true },
        New("basic-spear", "长枪", EquipmentSlot.MainHand, new(2, 0, 0, 0), new(1, 0, 0, 0), 200),
        New("basic-sword", "短剑", EquipmentSlot.OffHand, new(1, 0, 0, 0), new(1, 0, 0, 0), 180),
        New("basic-bow", "短弓", EquipmentSlot.OffHand, new(1, 0, 0, 0), new(1, 0, 0, 0), 220),
        New("basic-dart", "飞镖", EquipmentSlot.HiddenWeapon, new(1, 0, 0, 0), new(1, 0, 0, 0), 180),
        New("basic-shield", "木盾", EquipmentSlot.OffHand, new(0, 0, 0, 1), new(0, 0, 0, 1), 180),
        New("basic-helmet", "铁盔", EquipmentSlot.Helmet, new(0, 0, 0, 1), new(0, 0, 0, 1), 160),
        New("basic-armor", "皮铠", EquipmentSlot.Armor, new(0, 0, 0, 2), new(0, 0, 0, 1), 240),
        New("basic-mount", "战马", EquipmentSlot.Mount, new(1, 0, 0, 0), default, 250),
        New("yellow-horse", "黄鬃", EquipmentSlot.Mount, new(1, 0, 0, 0), default, 230),
        Advanced("fine-spear", "精铁枪", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), 450, new(60, 0, 0, 0)),
        Advanced("long-sword", "长剑", EquipmentSlot.MainHand, new(2, 0, 0, 0), new(1, 0, 0, 0), 360, new(50, 0, 0, 0)),
        Advanced("pointed-saber", "眉尖刀", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), 420, new(55, 0, 0, 0)),
        Advanced("hook-saber", "钩镰刀", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), 460, new(60, 0, 0, 0)),
        Advanced("twin-halberds", "双戟", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), 440, new(60, 0, 0, 0)),
        Advanced("hook-spear", "单钩枪", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), 430, new(55, 0, 0, 0)),
        Advanced("fine-sword", "精铁剑", EquipmentSlot.OffHand, new(2, 0, 0, 0), new(1, 0, 0, 0), 400, new(55, 0, 0, 0)),
        Advanced("fine-bow", "强弓", EquipmentSlot.OffHand, new(2, 0, 0, 0), new(1, 0, 0, 0), 480, new(65, 0, 0, 0)),
        Advanced("fine-helmet", "精铁盔", EquipmentSlot.Helmet, new(0, 0, 0, 2), new(0, 0, 0, 1), 380, new(0, 0, 0, 55)),
        Advanced("fine-armor", "铁铠", EquipmentSlot.Armor, new(0, 0, 0, 3), new(0, 0, 0, 1), 500, new(0, 0, 0, 60)),
        Advanced("fine-mount", "良马", EquipmentSlot.Mount, new(2, 0, 0, 0), default, 550, new(60, 0, 0, 0)),
        Advanced("iron-shield", "铁盾", EquipmentSlot.OffHand, new(0, 0, 0, 2), new(0, 0, 0, 1), 400, new(0, 0, 0, 55)),
        Advanced("great-sword", "大剑", EquipmentSlot.MainHand, new(4, 0, 0, 0), new(1, 0, 0, 0), 700, new(70, 0, 0, 0)) with { Grade = 3 },
        Advanced("trident-saber", "三尖刀", EquipmentSlot.MainHand, new(4, 0, 0, 0), new(1, 0, 0, 0), 700, new(70, 0, 0, 0)) with { Grade = 3 },
        Advanced("double-hook-spear", "双钩枪", EquipmentSlot.MainHand, new(4, 0, 0, 0), new(1, 0, 0, 0), 700, new(70, 0, 0, 0)) with { Grade = 3 },
        Advanced("heavy-armor", "甲胄", EquipmentSlot.Armor, new(0, 0, 0, 4), new(0, 0, 0, 1), 750, new(0, 0, 0, 70)) with { Grade = 3 },
        Advanced("swift-horse", "骏马", EquipmentSlot.Mount, new(3, 0, 0, 0), default, 800, new(70, 0, 0, 0)) with { Grade = 3 }
    ];

    private static EquipmentDefinition New(string id, string name, EquipmentSlot slot, OfficerAbilities bonus,
        OfficerAbilities growth, int cost) => new(id, slot, EquipmentRarity.Ordinary, bonus, growth)
        { Name = name, ResearchProjectId = ResearchId, ProductionCost = cost, ProductionDays = 30, PurchaseCost = checked(cost * 2) };

    private static EquipmentDefinition Advanced(string id, string name, EquipmentSlot slot, OfficerAbilities bonus,
        OfficerAbilities growth, int cost, OfficerAbilities minimum) => New(id, name, slot, bonus, growth, cost) with
        { Grade = 2, MinimumAbilities = minimum, PurchaseResearchProjectId = ResearchId };
}
