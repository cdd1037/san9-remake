using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum OfficerStatus { Ruler, Governor, Strategist, Serving, Captive, Free, Undiscovered, NotAppeared, Dead, Special }

public readonly record struct OfficerAbilities(int War, int Intelligence, int Politics, int Leadership)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNonnegative => War >= 0 && Intelligence >= 0 && Politics >= 0 && Leadership >= 0;
}

/// <summary>A person with runtime identity, allegiance and optional location. Captive allegiance remains the original force;
/// detention ownership is determined by the location. Names belong to content definitions.</summary>
public sealed record OfficerState(int Id, int? OwnerId, int? SettlementId, OfficerAbilities BaseAbilities, bool Acted)
{
    [System.Text.Json.Serialization.JsonRequired] public int? UnitId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int Affinity { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<int> MasteredCategories { get; init; } = [];
    public ImmutableArray<int> LearnedTactics { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public int? EquippedTacticId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int TacticCooldown { get; init; }
    public ImmutableArray<int> TacticProficiencies { get; init; } = [0, 0, 0, 0, 0, 0, 0, 0, 0];
    public int? StatusStartYear { get; init; }
    public int? ParentId { get; init; }
    public bool IsFemale { get; init; }
    public int Personality { get; init; }
    public int Injury { get; init; }
    public OfficerStatus Status { get; init; } = OfficerStatus.Serving;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsServing => Status <= OfficerStatus.Serving;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsVisible => Status <= OfficerStatus.Free;
    public int? RegionId { get; init; }
    public int BirthYear { get; init; }
    public int? OfficeId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsCommander { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ImmutableDictionary<int, int> ForceLoyalties { get; init; } = ImmutableDictionary<int, int>.Empty;
    [System.Text.Json.Serialization.JsonRequired] public int Righteousness { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool RewardedThisXun { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool HasRecruitmentPoliticsBonus { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public int Loyalty => OwnerId is int owner ? LoyaltyTo(owner) : 0;

    public int LoyaltyTo(int forceId) => ForceLoyalties.GetValueOrDefault(forceId);

    public OfficerState WithLoyalty(int forceId, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(forceId);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);
        return this with { ForceLoyalties = ForceLoyalties.SetItem(forceId, value) };
    }
    public bool IsValid()
    {
        if (Righteousness is < 0 or > 15 || ForceLoyalties is null ||
            ForceLoyalties.Any(p => p.Key < 0 || p.Value is < 0 or > 255)) return false;
        if (Affinity is < 0 or >= 150 || MasteredCategories.IsDefault ||
            MasteredCategories.Any(c => c is < 0 or > 8) || MasteredCategories.Distinct().Count() != MasteredCategories.Length) return false;
        if (EquippedTacticId < 0 || TacticCooldown < 0 || LearnedTactics.IsDefault || TacticProficiencies.IsDefault || TacticProficiencies.Length != 9 ||
            TacticProficiencies.Any(v => v < 0) || LearnedTactics.Any(v => v < 0) || LearnedTactics.Distinct().Count() != LearnedTactics.Length ||
            ParentId < 0 || Personality is < 0 or > 3) return false;
        if (Id < 0 || IsServing && SettlementId is null && UnitId is null || UnitId < 0 ||
            UnitId is not null && (SettlementId is not null || RegionId is not null || !IsServing && Status != OfficerStatus.Captive || IsCommander) ||
            SettlementId < 0 || OwnerId < 0 || RegionId < 0 || RegionId is not null && SettlementId is not null || !Enum.IsDefined(Status) || Injury is < 0 or > 14 ||
            !BaseAbilities.IsNonnegative) return false;
        return true;
    }
}
