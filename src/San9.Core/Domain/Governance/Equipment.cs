using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum EquipmentSlot { MainHand, OffHand, Helmet, Armor, Mount, Treasure, Tally, HiddenWeapon, Book }
public enum EquipmentRarity { Ordinary, Precious, Rare }
public enum EquipmentUse { Attack, Defense }
public enum EquipmentOrigin { Project, Historical, Romance }

/// <summary>Small independent effects on the wearer's unit; ranged defense only applies to ranged attacks.</summary>
public sealed record EquipmentCombatEffects(int AttackPercent = 0, int DefensePercent = 0,
    int MartialTacticPercent = 0, int RangedDefensePercent = 0)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => AttackPercent is >= 0 and <= 20 &&
        DefensePercent is >= 0 and <= 20 && MartialTacticPercent is >= 0 and <= 20 && RangedDefensePercent is >= 0 and <= 20;
}

/// <summary>Content-authored equipment type. Instances have independent identities and experience.</summary>
public sealed record EquipmentDefinition(string Id, EquipmentSlot Slot, EquipmentRarity Rarity,
    OfficerAbilities BaseBonuses, OfficerAbilities BonusPerLevel)
{
    public int? LegacyTreasureId { get; init; }
    public int? TacticId { get; init; }
    public bool EscapeMount { get; init; }
    public string Name { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentOrigin? Origin { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentCombatEffects? CombatEffects { get; init; }
    /// <summary>Fixed item grade, independent of rarity and experience level.</summary>
    public int Grade { get; init; } = 1;
    /// <summary>Issue one ordinary item in this slot when an old game or new scenario is initialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? InitialIssue { get; init; }
    /// <summary>Minimum innate officer abilities; equipped bonuses never count.</summary>
    public OfficerAbilities MinimumAbilities { get; init; }
    public string? ResearchProjectId { get; init; }
    public int ProductionCost { get; init; }
    public int ProductionDays { get; init; }
    /// <summary>Null means this item cannot be purchased; purchase and manufacture are independent.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PurchaseCost { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PurchaseResearchProjectId { get; init; }
    // Empty means no restrictions. Values are definition IDs, not source-game numeric slots.
    public ImmutableArray<string> CompatibleWeapons { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public bool IsOrdinaryArmament => LegacyTreasureId is null && Rarity == EquipmentRarity.Ordinary &&
        (PurchaseCost is not null || ResearchProjectId is not null || InitialIssue == true);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Enum.IsDefined(Slot) && Enum.IsDefined(Rarity) &&
        LegacyTreasureId is not < 0 && TacticId is not < 0 && (!EscapeMount || Slot == EquipmentSlot.Mount) &&
        Name is not null && (Origin is null || Enum.IsDefined(Origin.Value)) && (CombatEffects is null || CombatEffects.IsValid) && Grade is >= 1 and <= 5 && MinimumAbilities.IsNonnegative && ProductionCost >= 0 && ProductionDays >= 0 && PurchaseCost is not < 0 &&
        (InitialIssue != true || Rarity == EquipmentRarity.Ordinary && LegacyTreasureId is null && Grade == 1 &&
            Slot is EquipmentSlot.MainHand or EquipmentSlot.Helmet or EquipmentSlot.Armor && BaseBonuses == default && MinimumAbilities == default) &&
        (PurchaseCost is not null || PurchaseResearchProjectId is null) &&
        (PurchaseResearchProjectId is null || !string.IsNullOrWhiteSpace(PurchaseResearchProjectId)) &&
        (PurchaseCost is null || LegacyTreasureId is null && Rarity == EquipmentRarity.Ordinary && Slot != EquipmentSlot.Treasure) &&
        (ResearchProjectId is null ? ProductionCost == 0 && ProductionDays == 0 :
            !string.IsNullOrWhiteSpace(ResearchProjectId) && ProductionDays > 0 && LegacyTreasureId is null && Rarity == EquipmentRarity.Ordinary && Slot is not (EquipmentSlot.Treasure or EquipmentSlot.Tally or EquipmentSlot.Book)) &&
        BaseBonuses.IsNonnegative && BonusPerLevel.IsNonnegative && !CompatibleWeapons.IsDefault &&
        CompatibleWeapons.All(id => !string.IsNullOrWhiteSpace(id)) &&
        CompatibleWeapons.Distinct(StringComparer.Ordinal).Count() == CompatibleWeapons.Length &&
        (Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand || CompatibleWeapons.IsEmpty);
}

public sealed record EquipmentProductionProgress(string DefinitionId, int SettlementId, int DaysRemaining);

/// <summary>A unique object owned by either an officer or a force's inventory.</summary>
public sealed record EquipmentInstance(long Id, string DefinitionId, int? OfficerId, int? InventoryForceId,
    bool Equipped, int Level = 1, int Experience = 0)
{
    public int? LegacyTreasureId { get; init; }
    public bool IsLost { get; init; }
    /// <summary>Authored search location for an undiscovered independent item; null for an ordinary lost item.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? SearchPlaceId { get; init; }
    /// <summary>Unclaimed authored event reward; cleared whenever the item changes hands.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EventGiftKey { get; init; }
    /// <summary>Value of an ordinary item awaiting conversion when its holder enters service.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PendingArmamentCredit { get; init; }
    /// <summary>Initial or migrated item held while not serving; consumed on the first return to service.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? EquipOnService { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LastExperienceExchange { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedSet<string>? ExperienceExchanges { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Id >= 0 && !string.IsNullOrWhiteSpace(DefinitionId) &&
        LegacyTreasureId is not < 0 && (SearchPlaceId is null || SearchPlaceId >= 0 && IsLost && LegacyTreasureId is null && PendingArmamentCredit is null) &&
        (EventGiftKey is null || !string.IsNullOrWhiteSpace(EventGiftKey) && IsLost && SearchPlaceId is null && LegacyTreasureId is null && PendingArmamentCredit is null) &&
        (PendingArmamentCredit is null || PendingArmamentCredit >= 0 && LegacyTreasureId is null && !Equipped && EquipOnService != true) &&
        (LastExperienceExchange is null || !string.IsNullOrWhiteSpace(LastExperienceExchange)) &&
        (ExperienceExchanges is null || ExperienceExchanges.Count > 0 && ExperienceExchanges.All(value => !string.IsNullOrWhiteSpace(value))) &&
        OfficerId is not < 0 && InventoryForceId is not < 0 &&
        !(OfficerId is not null && InventoryForceId is not null) &&
        (OfficerId is not null || InventoryForceId is not null || LegacyTreasureId is not null || IsLost) &&
        (!IsLost || OfficerId is null && InventoryForceId is null && LegacyTreasureId is null && !Equipped) &&
        (!Equipped || OfficerId is not null) && (EquipOnService != true || OfficerId is not null && !Equipped) && Level is >= 1 and <= 5 && Experience is >= 0 and < 100 &&
        (Level != 5 || Experience == 0);
}

public sealed class EquipmentRulebook
{
    public static EquipmentRulebook Empty { get; } = new([]);
    public ImmutableDictionary<string, EquipmentDefinition> Items { get; }

    public EquipmentRulebook(IEnumerable<EquipmentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var items = definitions.ToArray();
        if (items.Any(item => item is null || !item.IsValid) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Invalid equipment definitions.");
        Items = items.ToImmutableDictionary(item => item.Id, StringComparer.Ordinal);
        if (!EquipmentRules.ValidInventory([], Items) ||
            Items.Values.Where(item => item.LegacyTreasureId is not null).GroupBy(item => item.LegacyTreasureId).Any(g => g.Count() > 1) ||
            Items.Values.Where(item => item.InitialIssue == true).GroupBy(item => item.Slot).Any(g => g.Count() > 1))
            throw new ArgumentException("Invalid equipment definition references.");
    }
}

public static class EquipmentRules
{
    public static bool MeetsRequirements(EquipmentDefinition definition, OfficerAbilities abilities) =>
        abilities.War >= definition.MinimumAbilities.War && abilities.Intelligence >= definition.MinimumAbilities.Intelligence &&
        abilities.Politics >= definition.MinimumAbilities.Politics && abilities.Leadership >= definition.MinimumAbilities.Leadership;

    public static bool ValidInventory(IEnumerable<EquipmentInstance> items,
        IReadOnlyDictionary<string, EquipmentDefinition> definitions)
    {
        var all = items.ToArray();
        if (definitions.Any(pair => pair.Key != pair.Value.Id || !pair.Value.IsValid) ||
            definitions.Values.Any(definition => definition.CompatibleWeapons.Any(id =>
                !definitions.TryGetValue(id, out var other) ||
                other.Slot != (definition.Slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand))) ||
            all.Any(item => !item.IsValid || !definitions.ContainsKey(item.DefinitionId)) ||
            all.Any(item => !ValidLevel(item, definitions[item.DefinitionId])) ||
            all.Any(item => item.LegacyTreasureId != definitions[item.DefinitionId].LegacyTreasureId) ||
            all.Select(item => item.Id).Distinct().Count() != all.Length ||
            all.Where(item => item.LegacyTreasureId is not null).GroupBy(item => item.LegacyTreasureId).Any(group => group.Count() > 1))
            return false;
        foreach (var loadout in all.Where(item => item.Equipped).GroupBy(item => item.OfficerId!.Value))
        {
            if (loadout.GroupBy(item => definitions[item.DefinitionId].Slot).Any(group => group.Count() > 1)) return false;
            var main = loadout.FirstOrDefault(item => definitions[item.DefinitionId].Slot == EquipmentSlot.MainHand);
            var off = loadout.FirstOrDefault(item => definitions[item.DefinitionId].Slot == EquipmentSlot.OffHand);
            if (main is not null && off is not null && !Compatible(definitions[main.DefinitionId], definitions[off.DefinitionId])) return false;
        }
        return true;
    }

    public static bool Compatible(EquipmentDefinition main, EquipmentDefinition off) =>
        main.Slot == EquipmentSlot.MainHand && off.Slot == EquipmentSlot.OffHand &&
        (main.CompatibleWeapons.IsEmpty || main.CompatibleWeapons.Contains(off.Id, StringComparer.Ordinal)) &&
        (off.CompatibleWeapons.IsEmpty || off.CompatibleWeapons.Contains(main.Id, StringComparer.Ordinal));

    /// <summary>Returns a replacement loadout, or null when the requested change is invalid.</summary>
    public static ImmutableArray<EquipmentInstance>? Equip(IEnumerable<EquipmentInstance> items,
        IReadOnlyDictionary<string, EquipmentDefinition> definitions, int officerId, long itemId)
    {
        var all = items.ToImmutableArray();
        if (officerId < 0 || !ValidInventory(all, definitions))
            return null;
        var target = all.FirstOrDefault(item => item.Id == itemId);
        if (target is null || target.OfficerId != officerId || target.PendingArmamentCredit is not null) return null;
        var definition = definitions[target.DefinitionId];
        if (definition.IsOrdinaryArmament) return null;
        var equipped = all.Where(item => item.OfficerId == officerId && item.Equipped && item.Id != target.Id &&
            definitions[item.DefinitionId].Slot != definition.Slot).ToArray();
        if (equipped.GroupBy(item => definitions[item.DefinitionId].Slot).Any(group => group.Count() > 1)) return null;
        var other = equipped.FirstOrDefault(item => definitions[item.DefinitionId].Slot ==
            (definition.Slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand));
        if (other is not null && (definition.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand) &&
            !(definition.Slot == EquipmentSlot.MainHand ? Compatible(definition, definitions[other.DefinitionId]) : Compatible(definitions[other.DefinitionId], definition)))
            return null;
        return all.Select(item => item.Id == target.Id ? item with { Equipped = true } :
            item.OfficerId == officerId && item.Equipped && definitions[item.DefinitionId].Slot == definition.Slot ? item with { Equipped = false } : item).ToImmutableArray();
    }

    public static EquipmentInstance MoveToOfficer(EquipmentInstance item, int officerId)
    {
        if (!item.IsValid || officerId < 0) throw new ArgumentException("Invalid equipment transfer.");
        return item with { OfficerId = officerId, InventoryForceId = null, Equipped = false, IsLost = false, SearchPlaceId = null, EventGiftKey = null, EquipOnService = null };
    }

    public static EquipmentInstance MoveToInventory(EquipmentInstance item, int forceId)
    {
        if (!item.IsValid || forceId < 0) throw new ArgumentException("Invalid equipment transfer.");
        return item with { OfficerId = null, InventoryForceId = forceId, Equipped = false, IsLost = false, SearchPlaceId = null, EventGiftKey = null, EquipOnService = null };
    }

    public static OfficerAbilities Bonuses(IEnumerable<EquipmentInstance> items,
        IReadOnlyDictionary<string, EquipmentDefinition> definitions, int officerId)
    {
        var all = items.ToArray();
        if (officerId < 0 || !ValidInventory(all, definitions)) throw new ArgumentException("Invalid equipment inventory.");
        return SumBonuses(all.Where(item => item.Equipped && item.OfficerId == officerId), definitions);
    }

    public static ImmutableDictionary<int, OfficerAbilities> BonusesByOfficer(IEnumerable<EquipmentInstance> items,
        IReadOnlyDictionary<string, EquipmentDefinition> definitions)
    {
        var all = items.ToArray();
        if (!ValidInventory(all, definitions)) throw new ArgumentException("Invalid equipment inventory.");
        return all.Where(item => item.Equipped).GroupBy(item => item.OfficerId!.Value)
            .ToImmutableDictionary(group => group.Key, group => SumBonuses(group, definitions));
    }

    private static OfficerAbilities SumBonuses(IEnumerable<EquipmentInstance> equipped,
        IReadOnlyDictionary<string, EquipmentDefinition> definitions)
    {
        long war = 0, intelligence = 0, politics = 0, leadership = 0;
        foreach (var item in equipped)
        {
            var definition = definitions[item.DefinitionId];
            int growth = item.Level - 1;
            war += (long)definition.BaseBonuses.War + (long)definition.BonusPerLevel.War * growth;
            intelligence += (long)definition.BaseBonuses.Intelligence + (long)definition.BonusPerLevel.Intelligence * growth;
            politics += (long)definition.BaseBonuses.Politics + (long)definition.BonusPerLevel.Politics * growth;
            leadership += (long)definition.BaseBonuses.Leadership + (long)definition.BonusPerLevel.Leadership * growth;
        }
        return new((int)Math.Min(int.MaxValue, war), (int)Math.Min(int.MaxValue, intelligence),
            (int)Math.Min(int.MaxValue, politics), (int)Math.Min(int.MaxValue, leadership));
    }

    public static EquipmentInstance RecordUse(EquipmentInstance item, EquipmentDefinition definition, EquipmentUse use)
    {
        if (!item.IsValid || !definition.IsValid || item.DefinitionId != definition.Id || !ValidLevel(item, definition) || !Enum.IsDefined(use))
            throw new ArgumentException("Invalid equipment use.");
        bool earns = item.Equipped && item.Level < 5 && (use switch
        {
            EquipmentUse.Attack => definition.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.HiddenWeapon,
            EquipmentUse.Defense => definition.Slot is EquipmentSlot.Helmet or EquipmentSlot.Armor,
            _ => false
        });
        if (!earns) return item;
        int experience = item.Experience + 1;
        return experience == 100 ? item with { Level = item.Level + 1, Experience = 0 } : item with { Experience = experience };
    }

    private static bool ValidLevel(EquipmentInstance item, EquipmentDefinition definition) =>
        definition.Slot is not (EquipmentSlot.Mount or EquipmentSlot.Treasure or EquipmentSlot.Tally or EquipmentSlot.Book) || item.Level == 1 && item.Experience == 0;
}
