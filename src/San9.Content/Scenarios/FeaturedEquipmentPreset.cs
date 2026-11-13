using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Project-authored items. Placement is a game rule, not a claim of historical provenance.</summary>
public static class FeaturedEquipmentPreset
{
    private static EquipmentDefinition Item(string id, string name, EquipmentSlot slot, OfficerAbilities bonus,
        OfficerAbilities growth, OfficerAbilities minimum, int grade = 2) =>
        new($"featured-{id}", slot, grade >= 3 ? EquipmentRarity.Rare : EquipmentRarity.Precious, bonus, growth)
        { Name = name, Origin = EquipmentOrigin.Project, Grade = grade, MinimumAbilities = minimum };

    public static ImmutableArray<EquipmentDefinition> Definitions() =>
    [
        Item("xuanwen-ji", "玄纹长戟", EquipmentSlot.MainHand, new(3, 0, 0, 1), new(1, 0, 0, 0), new(60, 0, 0, 0)) with { CombatEffects = new(AttackPercent: 3) },
        Item("qinggang-jian", "青罡剑", EquipmentSlot.MainHand, new(2, 1, 0, 0), new(1, 0, 0, 0), new(50, 0, 0, 0)),
        Item("yanling-dao", "长锋环首刀", EquipmentSlot.MainHand, new(3, 0, 0, 0), new(1, 0, 0, 0), new(55, 0, 0, 0)),
        Item("cangyun-qiang", "苍云枪", EquipmentSlot.MainHand, new(3, 0, 0, 1), new(1, 0, 0, 0), new(60, 0, 0, 0)),
        Item("chifeng-mao", "赤锋矛", EquipmentSlot.MainHand, new(4, 0, 0, 0), new(1, 0, 0, 0), new(70, 0, 0, 0), 3) with { CombatEffects = new(MartialTacticPercent: 5) },
        Item("huyue-ji", "虎跃戟", EquipmentSlot.MainHand, new(4, 0, 0, 1), new(1, 0, 0, 0), new(75, 0, 0, 0), 3) with { CombatEffects = new(AttackPercent: 4) },
        Item("ningyuan-jian", "宁远剑", EquipmentSlot.MainHand, new(2, 0, 0, 2), new(1, 0, 0, 0), new(55, 0, 0, 45)),
        Item("qingyu-dao", "青羽刀", EquipmentSlot.MainHand, new(3, 1, 0, 0), new(1, 0, 0, 0), new(60, 40, 0, 0)),
        Item("yueshan-yue", "越山钺", EquipmentSlot.MainHand, new(4, 0, 0, 0), new(1, 0, 0, 0), new(75, 0, 0, 0), 3),
        Item("jingtao-qiang", "惊涛枪", EquipmentSlot.MainHand, new(3, 0, 0, 1), new(1, 0, 0, 0), new(65, 0, 0, 0)),
        Item("shuofeng-ji", "朔风戟", EquipmentSlot.MainHand, new(4, 0, 0, 1), new(1, 0, 0, 0), new(75, 0, 0, 0), 3),
        Item("yunwen-jian", "云纹剑", EquipmentSlot.OffHand, new(1, 1, 1, 0), new(1, 0, 0, 0), new(45, 45, 0, 0)),
        Item("huanshou-kui", "铁叶兜鍪", EquipmentSlot.Helmet, new(0, 0, 0, 2), new(0, 0, 0, 1), new(0, 0, 0, 50)) with { CombatEffects = new(RangedDefensePercent: 3) },
        Item("yunwen-kui", "云纹兜鍪", EquipmentSlot.Helmet, new(0, 1, 0, 2), new(0, 0, 0, 1), new(0, 0, 0, 60)),
        Item("xuantie-jia", "玄铁札甲", EquipmentSlot.Armor, new(1, 0, 0, 3), new(0, 0, 0, 1), new(55, 0, 0, 55), 3) with { CombatEffects = new(DefensePercent: 4) },
        Item("qinglin-jia", "青鳞甲", EquipmentSlot.Armor, new(0, 0, 0, 3), new(0, 0, 0, 1), new(0, 0, 0, 60)) with { CombatEffects = new(RangedDefensePercent: 4) },
        Item("chijin-jia", "赤锦明甲", EquipmentSlot.Armor, new(1, 0, 0, 3), new(0, 0, 0, 1), new(0, 0, 0, 70), 3),
        Item("wuzhui", "乌鬃", EquipmentSlot.Mount, new(2, 0, 0, 1), default, new(55, 0, 0, 0)),
        Item("huacong", "花骢", EquipmentSlot.Mount, new(2, 0, 0, 0), default, new(50, 0, 0, 0)),
        Item("zaoliu", "枣骝", EquipmentSlot.Mount, new(1, 0, 0, 1), default, new(45, 0, 0, 0)),
        Item("qingzong", "青鬃", EquipmentSlot.Mount, new(2, 0, 0, 1), default, new(60, 0, 0, 0)),
        Item("xuecong", "雪骢", EquipmentSlot.Mount, new(3, 0, 0, 1), default, new(70, 0, 0, 0), 3),
        Item("heiliu", "黑骝", EquipmentSlot.Mount, new(2, 0, 0, 1), default, new(55, 0, 0, 0)),
        Item("huwen-fu", "虎纹兵符", EquipmentSlot.Tally, new(0, 0, 0, 2), default, new(0, 0, 0, 55)),
        Item("yunlei-fu", "云雷兵符", EquipmentSlot.Tally, new(0, 1, 0, 2), default, new(0, 50, 0, 60)),
        Item("shanwen-fu", "山纹兵符", EquipmentSlot.Tally, new(0, 0, 1, 2), default, new(0, 0, 45, 55)),
        Item("shuanghu-fu", "双虎兵符", EquipmentSlot.Tally, new(0, 0, 0, 3), default, new(0, 0, 0, 70), 3) with { CombatEffects = new(AttackPercent: 2, DefensePercent: 2) },
        Item("yanwei-biao", "雁尾镖", EquipmentSlot.HiddenWeapon, new(2, 0, 0, 0), new(1, 0, 0, 0), new(50, 0, 0, 0)),
        Item("liuxing-biao", "流星镖", EquipmentSlot.HiddenWeapon, new(2, 1, 0, 0), new(1, 0, 0, 0), new(55, 0, 0, 0)),
        Item("heiling-zhen", "黑翎针", EquipmentSlot.HiddenWeapon, new(1, 2, 0, 0), new(0, 1, 0, 0), new(45, 55, 0, 0)),
        Item("feishi-suo", "飞石索", EquipmentSlot.HiddenWeapon, new(2, 0, 0, 1), new(1, 0, 0, 0), new(55, 0, 0, 0)),
        Item("xingjun-ji", "行军记", EquipmentSlot.Book, new(0, 1, 0, 1), default, new(0, 45, 0, 40)),
        Item("shoubei-lue", "守备略", EquipmentSlot.Book, new(0, 1, 0, 2), default, new(0, 55, 0, 50)),
        Item("junxu-ce", "军需策", EquipmentSlot.Book, new(0, 1, 2, 0), default, new(0, 50, 55, 0)),
        Item("dili-yao", "地理要览", EquipmentSlot.Book, new(0, 2, 1, 0), default, new(0, 55, 45, 0)),
        Item("jianglue-pian", "将略篇", EquipmentSlot.Book, new(0, 2, 0, 2), default, new(0, 65, 0, 60), 3) with { CombatEffects = new(MartialTacticPercent: 4) },
        Item("wenyu-pei", "温玉佩", EquipmentSlot.Treasure, new(0, 0, 1, 1), default, new(0, 0, 40, 40)),
        Item("qingyu-yin", "青玉印", EquipmentSlot.Treasure, new(0, 0, 2, 0), default, new(0, 0, 55, 0)),
        Item("tongque-zun", "铜雀尊", EquipmentSlot.Treasure, new(0, 1, 1, 0), default, new(0, 45, 45, 0)),
        Item("guwen-jing", "古纹镜", EquipmentSlot.Treasure, new(0, 2, 1, 0), default, new(0, 60, 50, 0), 3)
    ];

    /// <summary>Deterministically distributes unique project items among active cities in a converted scenario.</summary>
    public static ScenarioInitialState Apply(ScenarioInitialState scenario)
    {
        var definitions = Definitions();
        if (scenario.Information.Equipment.Any(d => definitions.Any(item => item.Id == d.Id)))
            throw new InvalidDataException("Featured equipment IDs already exist.");
        int[] cities = scenario.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant && s.CanHostPersons)
            .Select(s => s.Id).Order().ToArray();
        if (cities.Length == 0) throw new InvalidDataException("No active city for featured equipment.");
        long last = scenario.Information.TreasureStates.Select(t => (long)t.Id)
            .Concat((scenario.InitialEquipment ?? []).Select(item => item.Id)).DefaultIfEmpty(-1).Max();
        var eventItems = new HashSet<string>(StringComparer.Ordinal)
            { "featured-shuanghu-fu", "featured-feishi-suo", "featured-jianglue-pian", "featured-guwen-jing" };
        var initial = definitions.Select((definition, index) => new EquipmentInstance(checked(last + 1 + index), definition.Id, null, null, false)
            { IsLost = true, SearchPlaceId = eventItems.Contains(definition.Id) ? null : cities[index % cities.Length],
                EventGiftKey = eventItems.Contains(definition.Id) ? $"equipment-gift.{definition.Id}" : null }).ToArray();
        var gifts = initial.Select((item, index) => (item, index)).Where(pair => eventItems.Contains(pair.item.DefinitionId))
            .Select(pair => new EquipmentGiftSearchEventDefinition(cities[pair.index % cities.Length], pair.item.Id,
                pair.item.DefinitionId, new($"equipment-gift.{pair.item.DefinitionId}"))
            {
                Id = $"equipment-gift.{pair.item.DefinitionId}", Priority = -40, Consumption = San9.Core.Simulation.EventConsumption.OnSuccess,
                ChanceAbility = CommunityAbility.Politics, ChanceDivisor = 5
            }).ToArray();
        return scenario with { Information = scenario.Information with { Equipment = scenario.Information.Equipment.AddRange(definitions) },
            InitialEquipment = (scenario.InitialEquipment ?? []).Concat(initial).ToArray(), SearchEvents = scenario.SearchEvents.AddRange(gifts) };
    }
}
