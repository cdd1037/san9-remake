using System.Collections.Immutable;
using System.Text.Json.Serialization;

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
    [JsonRequired] public int? UnitId { get; init; }
    [JsonRequired] public int Affinity { get; init; }
    [JsonRequired] public int TacticLinkBonus { get; init; }
    [JsonRequired] public DuelAdvantages DuelAdvantages { get; init; }
    [JsonRequired] public ImmutableArray<int> MasteredCategories { get; init; } = [];
    public ImmutableArray<int> LearnedTactics { get; init; } = [];
    [JsonRequired] public int? EquippedTacticId { get; init; }
    [JsonRequired] public int TacticCooldown { get; init; }
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
    [JsonRequired] public int LifespanTier { get; init; }
    [JsonRequired] public int SuccessionPriority { get; init; }
    [JsonRequired] public int AppearanceYear { get; init; }
    // A family identity, not a reference to a required living officer.
    [JsonRequired] public int? BloodlineId { get; init; }
    public int? OfficeId { get; init; }
    [JsonRequired] public bool IsCommander { get; init; }
    [JsonRequired] public ImmutableDictionary<int, int> ForceLoyalties { get; init; } = ImmutableDictionary<int, int>.Empty;
    [JsonRequired] public int Righteousness { get; init; }
    [JsonRequired] public int Ambition { get; init; }
    [JsonRequired] public int ForceFounderPriority { get; init; }
    [JsonRequired] public bool PrefersConsolidationLeadership { get; init; }
    [JsonRequired] public bool AutomaticSurrenderAllowed { get; init; } = true;
    [JsonRequired] public bool RewardedThisXun { get; init; }
    [JsonRequired] public bool HasRecruitmentPoliticsBonus { get; init; }
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
        if ((DuelAdvantages & ~(DuelAdvantages.DominantParry | DuelAdvantages.DominantAttack | DuelAdvantages.DominantDoubleStrike)) != 0) return false;
        if (Status == OfficerStatus.NotAppeared && SettlementId is null && RegionId is null) return false;
        if (ForceFounderPriority < 0 || Ambition is < 0 or > 15 || Righteousness is < 0 or > 15 || ForceLoyalties is null ||
            ForceLoyalties.Any(p => p.Key < 0 || p.Value is < 0 or > 255)) return false;
        if (Affinity is < 0 or >= 150 || TacticLinkBonus is < 0 or > 100 || MasteredCategories.IsDefault ||
            MasteredCategories.Any(c => c is < 0 or > 8) || MasteredCategories.Distinct().Count() != MasteredCategories.Length) return false;
        if (EquippedTacticId < 0 || TacticCooldown < 0 || LearnedTactics.IsDefault || TacticProficiencies.IsDefault || TacticProficiencies.Length != 9 ||
            TacticProficiencies.Any(v => v < 0) || LearnedTactics.Any(v => v < 0) || LearnedTactics.Distinct().Count() != LearnedTactics.Length ||
            ParentId < 0 || BloodlineId < 0 || AppearanceYear < 0 || LifespanTier < 0 || SuccessionPriority < 0 || Personality is < 0 or > 3) return false;
        if (Id < 0 || IsServing && SettlementId is null && UnitId is null || UnitId < 0 ||
            UnitId is not null && (SettlementId is not null || RegionId is not null || !IsServing && Status != OfficerStatus.Captive || IsCommander) ||
            SettlementId < 0 || OwnerId < 0 || RegionId < 0 || RegionId is not null && SettlementId is not null || !Enum.IsDefined(Status) || Injury is < 0 or > 14 ||
            !BaseAbilities.IsNonnegative) return false;
        return true;
    }
}
