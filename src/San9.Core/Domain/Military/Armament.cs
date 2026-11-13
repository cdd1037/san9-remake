using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum ArmamentSlot { Weapon, Armor }
public sealed record ArmamentProductionProgress(string DefinitionId, int SettlementId, uint Batches, int DaysRemaining);

/// <summary>A content-authored issue of ordinary equipment, counted in individual sets.</summary>
public sealed record ArmamentDefinition(string Id, string Name, ArmamentSlot Slot,
    ImmutableHashSet<int> TroopTypeIds, int AttackBonus, int DefenseBonus,
    int PurchaseCostPerThousand, string? ResearchProjectId)
{
    public int? ProductionCostPerThousand { get; init; }
    public int? ProductionDays { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name) &&
        Enum.IsDefined(Slot) && TroopTypeIds is not null && TroopTypeIds.All(id => id >= 0) &&
        AttackBonus is >= 0 and <= 100 && DefenseBonus is >= 0 and <= 100 && PurchaseCostPerThousand >= 0 &&
        (ResearchProjectId is null || !string.IsNullOrWhiteSpace(ResearchProjectId)) &&
        (ProductionCostPerThousand is null && ProductionDays is null ||
            ProductionCostPerThousand is >= 0 && ProductionDays is > 0);

    public bool Supports(int troopTypeId) => TroopTypeIds.IsEmpty || TroopTypeIds.Contains(troopTypeId);
}

/// <summary>
/// An allocation tracks its original strength and cumulative losses. Calculating losses from
/// the original issue makes losses independent of how a battle is split into exchanges.
/// A fractional lost set rounds up, so returning and reissuing cannot recover it.
/// Reinforcements do not acquire equipment without a separate issue.
/// </summary>
public sealed record ArmamentAllocation(string DefinitionId, uint IssuedSets, uint InitialSoldiers, uint Casualties)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(DefinitionId) && InitialSoldiers > 0 &&
        IssuedSets <= InitialSoldiers && Casualties <= InitialSoldiers;

    [System.Text.Json.Serialization.JsonIgnore] public uint RemainingSets => IsValid
        ? IssuedSets - (uint)(((ulong)IssuedSets * Casualties + InitialSoldiers - 1) / InitialSoldiers)
        : throw new InvalidOperationException("Invalid armament allocation.");

    public ArmamentAllocation ApplyCasualties(uint count)
    {
        if (!IsValid || count > InitialSoldiers - Casualties)
            throw new ArgumentOutOfRangeException(nameof(count));
        return this with { Casualties = Casualties + count };
    }

    /// <summary>Scales a percentage bonus in basis points without floating point.</summary>
    public int EffectiveBonusBasisPoints(int fullBonus, uint soldiers)
    {
        if (fullBonus is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(fullBonus));
        if (soldiers == 0) return 0;
        return (int)((ulong)fullBonus * 100 * Math.Min(RemainingSets, soldiers) / soldiers);
    }
}

/// <summary>Pure inventory operations; quantities are sets, not rounded batches.</summary>
public static class ArmamentInventory
{
    public const uint SetsPerBatch = 1000;

    public static ImmutableDictionary<string, uint> AddBatches(
        ImmutableDictionary<string, uint> inventory, string definitionId, uint batches)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("Invalid definition ID.", nameof(definitionId));
        return SetCount(inventory, definitionId, checked(inventory.GetValueOrDefault(definitionId) + checked(batches * SetsPerBatch)));
    }

    public static (ImmutableDictionary<string, uint> Inventory, ArmamentAllocation Allocation) Issue(
        ImmutableDictionary<string, uint> inventory, ArmamentDefinition definition, int troopTypeId, uint soldiers)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsValid || !definition.Supports(troopTypeId) || soldiers == 0)
            throw new ArgumentException("Invalid armament issue.");
        uint sets = Math.Min(soldiers, inventory.GetValueOrDefault(definition.Id));
        return (SetCount(inventory, definition.Id, inventory.GetValueOrDefault(definition.Id) - sets),
            new(definition.Id, sets, soldiers, 0));
    }

    public static ImmutableDictionary<string, uint> Return(
        ImmutableDictionary<string, uint> inventory, ArmamentAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(allocation);
        if (!allocation.IsValid) throw new ArgumentException("Invalid allocation.", nameof(allocation));
        return SetCount(inventory, allocation.DefinitionId,
            checked(inventory.GetValueOrDefault(allocation.DefinitionId) + allocation.RemainingSets));
    }

    /// <summary>Preserves returned sets above the addressable inventory limit in a separate reserve.</summary>
    public static (ImmutableDictionary<string, uint> Inventory, ImmutableDictionary<string, ulong> Reserve) ReturnWithReserve(
        ImmutableDictionary<string, uint> inventory, ImmutableDictionary<string, ulong> reserve, ArmamentAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(reserve);
        ArgumentNullException.ThrowIfNull(allocation);
        if (!allocation.IsValid) throw new ArgumentException("Invalid allocation.", nameof(allocation));
        string id = allocation.DefinitionId;
        uint current = inventory.GetValueOrDefault(id);
        uint accepted = Math.Min(uint.MaxValue - current, allocation.RemainingSets);
        ulong excess = (ulong)allocation.RemainingSets - accepted;
        if (excess > 0) reserve = reserve.SetItem(id, checked(reserve.GetValueOrDefault(id) + excess));
        return (SetCount(inventory, id, current + accepted), reserve);
    }

    /// <summary>Moves reserved sets back to inventory when room is available.</summary>
    public static (ImmutableDictionary<string, uint> Inventory, ImmutableDictionary<string, ulong> Reserve) ReleaseReserve(
        ImmutableDictionary<string, uint> inventory, ImmutableDictionary<string, ulong> reserve)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(reserve);
        foreach (var (id, quantity) in reserve.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(id) || quantity == 0) throw new ArgumentException("Invalid armament reserve.", nameof(reserve));
            uint available = uint.MaxValue - inventory.GetValueOrDefault(id);
            uint released = (uint)Math.Min((ulong)available, quantity);
            if (released == 0) continue;
            inventory = SetCount(inventory, id, inventory.GetValueOrDefault(id) + released);
            reserve = quantity == released ? reserve.Remove(id) : reserve.SetItem(id, quantity - released);
        }
        return (inventory, reserve);
    }

    public static bool CanReturnWithReserve(ImmutableDictionary<string, uint> inventory,
        ImmutableDictionary<string, ulong> reserve, IEnumerable<DetachmentState> detachments)
    {
        try
        {
            foreach (var detachment in detachments)
            foreach (var issue in new[] { detachment.Weapon, detachment.Armor })
                if (issue is not null) (inventory, reserve) = ReturnWithReserve(inventory, reserve, issue);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    /// <summary>Computes all returned sets before committing, so overflow cannot partially return a unit.</summary>
    public static ImmutableDictionary<string, uint>? TryReturnUnit(
        ImmutableDictionary<string, uint> inventory, IEnumerable<DetachmentState> detachments)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(detachments);
        try
        {
            foreach (var detachment in detachments)
            {
                if (detachment.Weapon is { } weapon) inventory = Return(inventory, weapon);
                if (detachment.Armor is { } armor) inventory = Return(inventory, armor);
            }
            return inventory;
        }
        catch (OverflowException) { return null; }
    }

    private static ImmutableDictionary<string, uint> SetCount(ImmutableDictionary<string, uint> inventory, string id, uint count) =>
        count == 0 ? inventory.Remove(id) : inventory.SetItem(id, count);
}
