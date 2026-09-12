using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record FacilityRule(int Id, int ConstructionCost, int MaxDurability, Footprint Footprint,
    int WoundedRecovery, bool Buildable, bool CanHostPersons, bool ConsumesFood, uint SoldierCapacity,
    int? RequiredTacticId, int? CostReductionTacticId, int? SelectedTacticId, ImmutableArray<int> TacticIds)
{
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool DestroyedAtZeroDurability { get; init; }
    internal void Validate(IReadOnlyDictionary<int, TacticRule> tactics)
    {
        Combat?.Validate();
        if (Attack is { Range: <= 0 } || Attack is not null && Combat is null) throw new ArgumentException("Invalid facility attack.");
        if (Id < 0 || ConstructionCost < 0 || MaxDurability < 2 || !Footprint.IsValid || WoundedRecovery < 0 ||
            SoldierCapacity == 0 || TacticIds.IsDefault || TacticIds.Distinct().Count() != TacticIds.Length ||
            TacticIds.Any(id => !tactics.ContainsKey(id)) || SelectedTacticId is int selected && !tactics.ContainsKey(selected) ||
            RequiredTacticId is int required && (!tactics.TryGetValue(required, out var requirement) || requirement.Effect != TacticEffect.StoneFormation) ||
            CostReductionTacticId is int reduction && (!tactics.TryGetValue(reduction, out var discount) || discount.Effect != TacticEffect.Construction))
            throw new ArgumentException("Invalid facility definition.");
    }

    public int Cost(int qualifiedMembers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(qualifiedMembers);
        return (int)(2L * ConstructionCost / (qualifiedMembers + 2L));
    }

    public int DailyProgress(uint soldiers, int leadership, int remaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leadership);
        ArgumentOutOfRangeException.ThrowIfNegative(remaining);
        if (remaining == 0 || soldiers == 0) return 0;
        long progress = EconomyMath.SquareRoot(soldiers) / 30 * (Math.Max(1L, leadership / 20) + 5);
        return (int)Math.Min(remaining, Math.Max(1L, progress));
    }
}

/// <summary>Static eligibility for a facility at an exact position; runtime ownership and occupancy are separate.</summary>
public sealed record ConstructionSite(int RegionId, WorldPosition Position, ImmutableArray<int> FacilityIds);

public sealed record FacilityInstance(int TypeId, WorldPosition Position,
    [property: System.Text.Json.Serialization.JsonRequired] int? BuilderUnitId = null);
