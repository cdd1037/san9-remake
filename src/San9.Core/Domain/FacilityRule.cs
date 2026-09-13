using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record FacilityRule(int Id, int ConstructionCost, int MaxDurability, Footprint Footprint,
    int WoundedRecovery, bool Buildable, bool CanHostPersons, bool ConsumesFood, uint SoldierCapacity,
    int? RequiredTacticId, int? CostReductionTacticId, int? SelectedTacticId, ImmutableArray<int> TacticIds)
{
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool DestroyedAtZeroDurability { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? ConstructionGrowthTacticId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public FacilityTactics? Tactics { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public FacilityAttackModifier? InnateAttack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsHarbor { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public FacilityReprisal? Reprisal { get; init; }
    internal void Validate(IReadOnlyDictionary<int, TacticRule> tactics)
    {
        Combat?.Validate();
        Tactics?.Validate(tactics);
        Reprisal?.Validate();
        if (Reprisal is not null && Combat is null) throw new ArgumentException("Facility reprisal requires combat properties.");
        if (InnateAttack is { RangeBonus: < 0 }) throw new ArgumentException("Invalid innate facility attack.");
        if (Attack is { Range: <= 0 } || Attack is not null && Combat is null) throw new ArgumentException("Invalid facility attack.");
        if (Id < 0 || ConstructionCost < 0 || MaxDurability < 2 || !Footprint.IsValid || WoundedRecovery < 0 ||
            SoldierCapacity == 0 || TacticIds.IsDefault || TacticIds.Distinct().Count() != TacticIds.Length ||
            TacticIds.Any(id => !tactics.ContainsKey(id)) || SelectedTacticId is int selected && !tactics.ContainsKey(selected) ||
            RequiredTacticId is int required && (!tactics.TryGetValue(required, out var requirement) || requirement.Effect != TacticEffect.StoneFormation) ||
            CostReductionTacticId is int reduction && (!tactics.TryGetValue(reduction, out var discount) || discount.Effect != TacticEffect.Construction) ||
            ConstructionGrowthTacticId is int growth && (!Buildable || !tactics.TryGetValue(growth, out var learning) ||
                learning.Effect is not (TacticEffect.Construction or TacticEffect.StoneFormation)))
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

/// <summary>Losses and confusion inflicted when a surviving facility takes siege damage.</summary>
public sealed record FacilityReprisal(int ProficiencyCategory, int ProficiencyThreshold, int DamageDivisor,
    int ConfusionChance, int IntelligenceDivisor, int ConfusionDays)
{
    internal void Validate()
    {
        if (ProficiencyCategory is < 0 or > 8 || ProficiencyThreshold < 0 || DamageDivisor <= 0 ||
            ConfusionChance is < 0 or > 100 || IntelligenceDivisor <= 0 || ConfusionDays <= 0)
            throw new ArgumentException("Invalid facility reprisal.");
    }

    public uint Damage(int proficiency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(proficiency);
        return (uint)Math.Max(1, (ProficiencyThreshold - (long)proficiency) / DamageDivisor);
    }
    public int Chance(int intelligence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intelligence);
        return Math.Clamp(ConfusionChance - intelligence / IntelligenceDivisor, 0, 100);
    }
}

public sealed record FacilityTactics(int OpportunityChance, int MartialCooldown, bool RequiresEnabledTactic,
    ImmutableArray<int> TacticPriority)
{
    internal void Validate(IReadOnlyDictionary<int, TacticRule> tactics)
    {
        if (OpportunityChance is < 0 or > 100 || MartialCooldown < 0 || TacticPriority.IsDefaultOrEmpty ||
            TacticPriority.Distinct().Count() != TacticPriority.Length || TacticPriority.Any(id =>
                !tactics.TryGetValue(id, out var tactic) || tactic.Effect is not (TacticEffect.Martial or TacticEffect.Confusion or
                    TacticEffect.Defection or TacticEffect.Illusion or TacticEffect.Taunt or TacticEffect.Sorcery or
                    TacticEffect.TrapSelection or TacticEffect.Trap or TacticEffect.Inspire or TacticEffect.Heal)))
            throw new ArgumentException("Invalid facility tactic profile.");
    }
}

/// <summary>Static eligibility for a facility at an exact position; runtime ownership and occupancy are separate.</summary>
public sealed record ConstructionSite(int RegionId, WorldPosition Position, ImmutableArray<int> FacilityIds);

public sealed record FacilityInstance(int TypeId, WorldPosition Position,
    [property: System.Text.Json.Serialization.JsonRequired] int? BuilderUnitId = null);
