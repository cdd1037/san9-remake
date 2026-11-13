using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum OfficerStatus { Ruler, Governor, Strategist, Serving, Captive, Free, Undiscovered, NotAppeared, Dead, Special }

/// <summary>An explicit player-configured future appearance, independent of historical scheduling.</summary>
public sealed record ManualOfficerAppearance(int Year, int SettlementId, int? ForceId, int Age)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool KeepPendingIfForceUnavailable { get; init; }
    [JsonIgnore] public bool IsValid => Year > 0 && SettlementId >= 0 && ForceId is not < 0 && Age is >= 1 and <= 100 && Age < Year;
}

public sealed record FormationTeachingProgress(int TeacherId, int ForceId, int SettlementId, int FormationId, int DaysRemaining);

public readonly record struct OfficerAbilities(int War, int Intelligence, int Politics, int Leadership)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNonnegative => War >= 0 && Intelligence >= 0 && Politics >= 0 && Leadership >= 0;
}

/// <summary>A person with runtime identity, allegiance and optional location. Captive allegiance remains the original force;
/// detention ownership is determined by the location. Names belong to content definitions.</summary>
public sealed record OfficerState(int Id, int? OwnerId, int? SettlementId, OfficerAbilities BaseAbilities, bool Acted)
{
    [JsonRequired] public bool IsDormant { get; init; }
    [JsonRequired] public int? UnitId { get; init; }
    [JsonRequired] public int Affinity { get; init; }
    [JsonRequired] public int TacticLinkBonus { get; init; }
    [JsonRequired] public DuelAdvantages DuelAdvantages { get; init; }
    [JsonRequired] public ImmutableArray<int> MasteredCategories { get; init; } = [];
    /// <summary>Permanent tactic membership; enumeration uses ascending IDs, independent of learning order.</summary>
    public ImmutableSortedSet<int> LearnedTactics { get; init; } = [];
    [JsonRequired] public int? EquippedTacticId { get; init; }
    /// <summary>兵法与单挑共用的剩余冷却天数；限制兵法发动、单挑发起及追加参战，
    /// 不限制单挑首发接受者或普通攻击。</summary>
    [JsonRequired] public int TacticCooldown { get; init; }
    /// <summary>Known formation IDs and individual proficiency; null denotes legacy data awaiting migration.</summary>
    public ImmutableSortedDictionary<int, int>? FormationProficiencies { get; init; }
    /// <summary>Days of practical instruction toward formations not yet mastered.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedDictionary<int, int>? FormationPractice { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FormationTeachingProgress? FormationTeaching { get; init; }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ManualOfficerAppearance? ManualAppearance { get; init; }
    // A family identity, not a reference to a required living officer.
    [JsonRequired] public int? BloodlineId { get; init; }
    [JsonRequired] public string Surname { get; init; } = "";
    public int? OfficeId { get; init; }
    /// <summary>Personal title retained independently of the officer's current appointment.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PersonalPeerage? PersonalPeerage { get; init; }
    /// <summary>Recorded battle merit by force; missing entries mean no recorded merit for that force.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedDictionary<int, int>? BattleMerit { get; init; }
    public int BattleMeritFor(int forceId) => BattleMerit?.GetValueOrDefault(forceId) ?? 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedDictionary<int, int>? ServiceMerit { get; init; }
    public int ServiceMeritFor(int forceId) => ServiceMerit?.GetValueOrDefault(forceId) ?? 0;
    [JsonRequired] public bool IsCommander { get; init; }
    private LoyaltyMap _loyalties = LoyaltyMap.Empty;
    // The wire contract contains only overrides. Keep incoming entries until the content baseline is bound.
    [JsonRequired] public ImmutableDictionary<int, int> ForceLoyalties
    {
        get => _loyalties.Overrides;
        init => _loyalties = _loyalties with { Overrides = value };
    }
    [JsonRequired] public int Righteousness { get; init; }
    [JsonRequired] public int Ambition { get; init; }
    [JsonRequired] public int ForceFounderPriority { get; init; }
    [JsonRequired] public bool PrefersConsolidationLeadership { get; init; }
    [JsonRequired] public bool AutomaticSurrenderAllowed { get; init; } = true;
    [JsonRequired] public bool RewardedThisXun { get; init; }
    [JsonRequired] public bool HasRecruitmentPoliticsBonus { get; init; }
    /// <summary>Binds content rule context on creation or load and removes redundant overrides.</summary>
    public OfficerState WithLoyaltyDefault(int defaultLoyalty)
    {
        var loyalties = _loyalties.Bind(defaultLoyalty);
        return ReferenceEquals(loyalties, _loyalties) ? this : this with { _loyalties = loyalties };
    }

    [JsonIgnore] public int Loyalty => OwnerId is int owner ? LoyaltyTo(owner) : 0;

    public int LoyaltyTo(int forceId) => _loyalties.Get(forceId);

    public OfficerState WithLoyalty(int forceId, int value)
    {
        var loyalties = _loyalties.Set(forceId, value);
        return ReferenceEquals(loyalties, _loyalties) ? this : this with { _loyalties = loyalties };
    }
    public bool IsValid()
    {
        if ((DuelAdvantages & ~(DuelAdvantages.DominantParry | DuelAdvantages.DominantAttack | DuelAdvantages.DominantDoubleStrike)) != 0) return false;
        if (Status is OfficerStatus.NotAppeared or OfficerStatus.Free && SettlementId is null && RegionId is null) return false;
        if (ForceFounderPriority < 0 || Ambition is < 0 or > 15 || Righteousness is < 0 or > 15 || ForceLoyalties is null ||
            ForceLoyalties.Any(p => p.Key < 0 || p.Value is < 0 or > 255)) return false;
        if (Affinity is < 0 or >= 150 || TacticLinkBonus is < 0 or > 100 || MasteredCategories.IsDefault ||
            MasteredCategories.Any(c => c is < 0 or > 8) || MasteredCategories.Distinct().Count() != MasteredCategories.Length) return false;
        if (FormationProficiencies is not null && FormationProficiencies.Any(p => p.Key < 0 || p.Value is < 0 or > 1000)) return false;
        if (FormationPractice is not null && FormationPractice.Any(p => p.Key < 0 || p.Value is < 1 or >= FormationKnowledge.PracticeDaysToLearn ||
            FormationProficiencies?.ContainsKey(p.Key) == true)) return false;
        if (BattleMerit is not null && (BattleMerit.IsEmpty || BattleMerit.Any(p => p.Key < 0 || p.Value is < 1 or > 1000000))) return false;
        if (ServiceMerit is not null && (ServiceMerit.IsEmpty || ServiceMerit.Any(p => p.Key < 0 || p.Value is < 1 or > 1000000))) return false;
        if (FormationTeaching is { } teaching && (teaching.TeacherId < 0 || teaching.TeacherId == Id || teaching.ForceId < 0 ||
            teaching.SettlementId < 0 || teaching.FormationId < 0 || teaching.DaysRemaining is < 1 or > 30)) return false;
        if (EquippedTacticId < 0 || TacticCooldown < 0 || LearnedTactics is null || TacticProficiencies.IsDefault || TacticProficiencies.Length != 9 ||
            TacticProficiencies.Any(v => v < 0) || LearnedTactics.Any(v => v < 0) ||
            ParentId < 0 || BloodlineId < 0 || AppearanceYear < 0 || LifespanTier < 0 || SuccessionPriority < 0 || Personality is < 0 or > 3) return false;
        if (PersonalPeerage is { IsValid: false } || ManualAppearance is { } manual && (!manual.IsValid || Status != OfficerStatus.NotAppeared)) return false;
        if (Surname is null || Id < 0 || IsServing && SettlementId is null && UnitId is null || UnitId < 0 ||
            UnitId is not null && (SettlementId is not null || RegionId is not null || !IsServing && Status != OfficerStatus.Captive || IsCommander) ||
            SettlementId < 0 || OwnerId < 0 || RegionId < 0 || RegionId is not null && SettlementId is not null || !Enum.IsDefined(Status) || Injury is < 0 or > 14 ||
            !BaseAbilities.IsNonnegative) return false;
        return true;
    }
}
