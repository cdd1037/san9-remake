using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record OfficerEquipmentItem(long Id, string Name, EquipmentSlot Slot, EquipmentRarity Rarity,
    int Level, int Experience, bool Equipped, bool InInventory, bool IsTreasure, string Effect,
    bool CanEquip, string? UnavailableReason, int Grade, string Requirements)
{
    public EquipmentOrigin? Origin { get; init; }
    public string DefinitionId { get; init; } = "";
}
public sealed record OfficerEquipmentSlot(EquipmentSlot Slot, string Name, OfficerEquipmentItem[] Items);
public sealed record OfficerEquipmentData(bool CanManage, OfficerEquipmentSlot[] Slots);

public static class OfficerEquipment
{
    public static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.MainHand => "主武器", EquipmentSlot.OffHand => "副武器", EquipmentSlot.Helmet => "头盔",
        EquipmentSlot.Armor => "铠甲", EquipmentSlot.Mount => "坐骑", EquipmentSlot.Treasure => "宝物",
        EquipmentSlot.Tally => "兵符", EquipmentSlot.HiddenWeapon => "暗器", EquipmentSlot.Book => "书籍",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    public static string Requirements(OfficerAbilities minimum)
    {
        List<string> parts = [];
        if (minimum.Leadership > 0) parts.Add($"统率{minimum.Leadership}");
        if (minimum.War > 0) parts.Add($"武力{minimum.War}");
        if (minimum.Intelligence > 0) parts.Add($"智力{minimum.Intelligence}");
        if (minimum.Politics > 0) parts.Add($"政治{minimum.Politics}");
        return parts.Count == 0 ? "无" : string.Join("、", parts);
    }

    public static OfficerEquipmentData Read(GameSession session, TextCatalog texts, int officerId)
    {
        var view = session.View;
        var world = view.World;
        var person = world.Officers.Single(p => p.Id == officerId);
        int? player = view.PlayerForceId;
        bool own = player is not null && person.OwnerId == player && person.IsServing;
        var rules = view.Rules.Equipment;
        string Name(EquipmentDefinition definition) => definition.LegacyTreasureId is int id
            ? texts.Resolve(view.Information.Treasures.Single(t => t.Id == id).NameKey)
            : string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name;
        string Effect(EquipmentDefinition definition, int level)
        {
            var b = definition.BaseBonuses; var g = definition.BonusPerLevel; int n = level - 1;
            List<string> effects = [];
            void Add(string label, long amount) { if (amount > 0) effects.Add($"{label}+{amount}"); }
            Add("统率", (long)b.Leadership + (long)g.Leadership * n);
            Add("武力", (long)b.War + (long)g.War * n);
            Add("智力", (long)b.Intelligence + (long)g.Intelligence * n);
            Add("政治", (long)b.Politics + (long)g.Politics * n);
            if (definition.TacticId is int tactic) effects.Add(view.Information.Tactics.FirstOrDefault(t => t.Id == tactic)?.Name ?? $"兵法 {tactic}");
            if (definition.EscapeMount) effects.Add("安然撤退");
            if (definition.CombatEffects is { } combat)
            {
                if (combat.AttackPercent > 0) effects.Add($"部队造成伤害+{combat.AttackPercent}%");
                if (combat.DefensePercent > 0) effects.Add($"部队受到伤害-{combat.DefensePercent}%");
                if (combat.MartialTacticPercent > 0) effects.Add($"武力兵法伤害+{combat.MartialTacticPercent}%");
                if (combat.RangedDefensePercent > 0) effects.Add($"远程受伤-{combat.RangedDefensePercent}%");
            }
            return effects.Count == 0 ? "无附加效果" : string.Join("、", effects);
        }
        OfficerEquipmentItem Item(EquipmentInstance instance)
        {
            var definition = rules.Items[instance.DefinitionId];
            string requirements = Requirements(definition.MinimumAbilities);
            string? unmet = EquipmentRules.MeetsRequirements(definition, person.BaseAbilities) ? null : $"基础属性不足，需要{requirements}。";
            bool inventory = instance.InventoryForceId is not null;
            string? incompatibility = null;
            if (definition.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand)
            {
                var otherSlot = definition.Slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand;
                var other = world.Equipment.FirstOrDefault(e => e.OfficerId == officerId && e.Equipped && rules.Items[e.DefinitionId].Slot == otherSlot);
                if (other is not null)
                {
                    var otherDefinition = rules.Items[other.DefinitionId];
                    bool compatible = definition.Slot == EquipmentSlot.MainHand
                        ? EquipmentRules.Compatible(definition, otherDefinition)
                        : EquipmentRules.Compatible(otherDefinition, definition);
                    if (!compatible) incompatibility = $"与已装备的{SlotName(otherSlot)}「{Name(otherDefinition)}」不兼容，请先更换或卸下。";
                }
            }
            bool allowed = own && incompatibility is null && unmet is null && session.Validate(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), player!.Value,
                world.Simulation.NextSequence, officerId, definition.Slot, instance.Id)).Accepted;
            return new(instance.Id, Name(definition), definition.Slot, definition.Rarity, instance.Level, instance.Experience,
                instance.Equipped, inventory, instance.LegacyTreasureId is not null,
                instance.PendingArmamentCredit is int credit ? $"入仕后转为军械余额 {credit}" : Effect(definition, instance.Level), allowed,
                instance.PendingArmamentCredit is not null ? "入仕后转换，暂不可装备。" : unmet ?? incompatibility ?? (!allowed ? "当前无法操作此装备。" : null), definition.Grade, requirements)
                { Origin = definition.Origin, DefinitionId = definition.Id };
        }
        var visible = world.Equipment.Where(e => e.OfficerId == officerId || own && e.InventoryForceId == player)
            .Select(Item).OrderByDescending(e => e.Equipped).ThenBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id).ToArray();
        return new(own, Enum.GetValues<EquipmentSlot>().Select(slot => new OfficerEquipmentSlot(slot, SlotName(slot),
            visible.Where(item => item.Slot == slot).ToArray())).ToArray());
    }
}
