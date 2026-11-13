using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Content.Scenarios;

/// <summary>Reusable person identity and default initial values, without ownership or world placement.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficerDefaults(int Id)
{
    [JsonRequired] public required string NameKey { get; init; }
    [JsonRequired] public required string Name { get; init; }
    [JsonRequired] public string? PortraitKey { get; init; }
    [JsonIgnore] public string GivenName => Name.StartsWith(Surname, StringComparison.Ordinal) ? Name[Surname.Length..] : Name;
    [JsonRequired] public required string? CourtesyName { get; init; }
    [JsonRequired] public required string? Biography { get; init; }
    [JsonRequired] public required int? AgeAtScenarioStart { get; init; }
    [JsonRequired] public required int FlagGlyph { get; init; }
    [JsonRequired] public required string? CourtesyNameKey { get; init; }
    [JsonRequired] public required string? BiographyKey { get; init; }
    [JsonRequired] public required int War { get; init; }
    [JsonRequired] public required int Intelligence { get; init; }
    [JsonRequired] public required int Politics { get; init; }
    [JsonRequired] public required int Leadership { get; init; }
    [JsonRequired] public required int Affinity { get; init; }
    [JsonRequired] public required int TacticLinkBonus { get; init; }
    [JsonRequired] public required DuelAdvantages DuelAdvantages { get; init; }
    [JsonRequired] public required ImmutableArray<int> MasteredCategories { get; init; }
    [JsonRequired] public required ImmutableSortedSet<int> LearnedTactics { get; init; }
    public ImmutableSortedDictionary<int, int>? FormationProficiencies { get; init; }
    [JsonRequired] public required ImmutableArray<int> TacticProficiencies { get; init; }
    [JsonRequired] public required int? ParentId { get; init; }
    [JsonRequired] public required bool IsFemale { get; init; }
    [JsonRequired] public required int Personality { get; init; }
    [JsonRequired] public required int BirthYear { get; init; }
    [JsonRequired] public required int LifespanTier { get; init; }
    [JsonRequired] public required int SuccessionPriority { get; init; }
    [JsonRequired] public required int AppearanceYear { get; init; }
    [JsonRequired] public required int? BloodlineId { get; init; }
    [JsonRequired] public required string Surname { get; init; }
    [JsonRequired] public required int Righteousness { get; init; }
    [JsonRequired] public required int Ambition { get; init; }
    [JsonRequired] public required int ForceFounderPriority { get; init; }
    [JsonRequired] public required bool PrefersConsolidationLeadership { get; init; }
    [JsonRequired] public required bool AutomaticSurrenderAllowed { get; init; }
    [JsonRequired] public required bool HasRecruitmentPoliticsBonus { get; init; }

    public static OfficerDefaults Create(int id) => new(id)
    {
        NameKey = $"authored.officer.{id}", Name = "新武将", Surname = "新", CourtesyName = null, Biography = null,
        PortraitKey = null, AgeAtScenarioStart = null, FlagGlyph = -1, CourtesyNameKey = null, BiographyKey = null,
        War = 50, Intelligence = 50, Politics = 50, Leadership = 50, Affinity = 75,
        TacticLinkBonus = 0, DuelAdvantages = DuelAdvantages.None, MasteredCategories = [], LearnedTactics = [],
        TacticProficiencies = [0, 0, 0, 0, 0, 0, 0, 0, 0], ParentId = null, IsFemale = false,
        Personality = 1, BirthYear = 170, LifespanTier = 7, SuccessionPriority = 0, AppearanceYear = 184,
        BloodlineId = id, Righteousness = 10, Ambition = 10, ForceFounderPriority = 0,
        PrefersConsolidationLeadership = false, AutomaticSurrenderAllowed = true, HasRecruitmentPoliticsBonus = false
    };

    public static OfficerDefaults From(OfficerInitialState value, TextCatalog texts) => new(value.State.Id)
    {
        NameKey = value.NameKey,
        Name = texts.Resolve(value.NameKey),
        PortraitKey = texts.MessagePeople.GetValueOrDefault(value.State.Id)?.PortraitKey,
        CourtesyName = value.CourtesyNameKey is { } courtesy ? texts.Resolve(courtesy) : null,
        Biography = value.BiographyKey is { } biography ? texts.ResolveRich(biography) : null,
        AgeAtScenarioStart = value.AgeAtScenarioStart,
        FlagGlyph = value.FlagGlyph,
        CourtesyNameKey = value.CourtesyNameKey,
        BiographyKey = value.BiographyKey,
        War = value.State.BaseAbilities.War,
        Intelligence = value.State.BaseAbilities.Intelligence,
        Politics = value.State.BaseAbilities.Politics,
        Leadership = value.State.BaseAbilities.Leadership,
        Affinity = value.State.Affinity,
        TacticLinkBonus = value.State.TacticLinkBonus,
        DuelAdvantages = value.State.DuelAdvantages,
        MasteredCategories = value.State.MasteredCategories,
        LearnedTactics = value.State.LearnedTactics,
        FormationProficiencies = value.State.FormationProficiencies,
        TacticProficiencies = value.State.TacticProficiencies,
        ParentId = value.State.ParentId,
        IsFemale = value.State.IsFemale,
        Personality = value.State.Personality,
        BirthYear = value.State.BirthYear,
        LifespanTier = value.State.LifespanTier,
        SuccessionPriority = value.State.SuccessionPriority,
        AppearanceYear = value.State.AppearanceYear,
        BloodlineId = value.State.BloodlineId,
        Surname = value.State.Surname,
        Righteousness = value.State.Righteousness,
        Ambition = value.State.Ambition,
        ForceFounderPriority = value.State.ForceFounderPriority,
        PrefersConsolidationLeadership = value.State.PrefersConsolidationLeadership,
        AutomaticSurrenderAllowed = value.State.AutomaticSurrenderAllowed,
        HasRecruitmentPoliticsBonus = value.State.HasRecruitmentPoliticsBonus,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficerDefaultsOverrides
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentOverride<string?>? PortraitKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<string>? NameKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentOverride<string>? Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentOverride<string?>? CourtesyName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentOverride<string?>? Biography { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int?>? AgeAtScenarioStart { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? FlagGlyph { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<string?>? CourtesyNameKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<string?>? BiographyKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? War { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Intelligence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Politics { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Leadership { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Affinity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? TacticLinkBonus { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<DuelAdvantages>? DuelAdvantages { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableArray<int>>? MasteredCategories { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableSortedSet<int>>? LearnedTactics { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableArray<int>>? TacticProficiencies { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableSortedDictionary<int, int>?>? FormationProficiencies { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int?>? ParentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<bool>? IsFemale { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Personality { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? BirthYear { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? LifespanTier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? SuccessionPriority { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? AppearanceYear { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int?>? BloodlineId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<string>? Surname { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Righteousness { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? Ambition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? ForceFounderPriority { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<bool>? PrefersConsolidationLeadership { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<bool>? AutomaticSurrenderAllowed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<bool>? HasRecruitmentPoliticsBonus { get; init; }

    public OfficerDefaults Apply(OfficerDefaults defaults) => defaults with
    {
        PortraitKey = PortraitKey is { } portrait ? portrait.Value : defaults.PortraitKey,
        NameKey = NameKey is { } changeNameKey ? changeNameKey.Value : defaults.NameKey,
        Name = Name is { } changeName ? changeName.Value : defaults.Name,
        CourtesyName = CourtesyName is { } changeCourtesy ? changeCourtesy.Value : defaults.CourtesyName,
        Biography = Biography is { } changeBiography ? changeBiography.Value : defaults.Biography,
        AgeAtScenarioStart = AgeAtScenarioStart is { } changeAgeAtScenarioStart ? changeAgeAtScenarioStart.Value : defaults.AgeAtScenarioStart,
        FlagGlyph = FlagGlyph is { } changeFlagGlyph ? changeFlagGlyph.Value : defaults.FlagGlyph,
        CourtesyNameKey = CourtesyNameKey is { } changeCourtesyNameKey ? changeCourtesyNameKey.Value : defaults.CourtesyNameKey,
        BiographyKey = BiographyKey is { } changeBiographyKey ? changeBiographyKey.Value : defaults.BiographyKey,
        War = War is { } changeWar ? changeWar.Value : defaults.War,
        Intelligence = Intelligence is { } changeIntelligence ? changeIntelligence.Value : defaults.Intelligence,
        Politics = Politics is { } changePolitics ? changePolitics.Value : defaults.Politics,
        Leadership = Leadership is { } changeLeadership ? changeLeadership.Value : defaults.Leadership,
        Affinity = Affinity is { } changeAffinity ? changeAffinity.Value : defaults.Affinity,
        TacticLinkBonus = TacticLinkBonus is { } changeTacticLinkBonus ? changeTacticLinkBonus.Value : defaults.TacticLinkBonus,
        DuelAdvantages = DuelAdvantages is { } changeDuelAdvantages ? changeDuelAdvantages.Value : defaults.DuelAdvantages,
        MasteredCategories = MasteredCategories is { } changeMasteredCategories ? changeMasteredCategories.Value : defaults.MasteredCategories,
        LearnedTactics = LearnedTactics is { } changeLearnedTactics ? changeLearnedTactics.Value : defaults.LearnedTactics,
        FormationProficiencies = FormationProficiencies is { } changeFormationProficiencies ? changeFormationProficiencies.Value : defaults.FormationProficiencies,
        TacticProficiencies = TacticProficiencies is { } changeTacticProficiencies ? changeTacticProficiencies.Value : defaults.TacticProficiencies,
        ParentId = ParentId is { } changeParentId ? changeParentId.Value : defaults.ParentId,
        IsFemale = IsFemale is { } changeIsFemale ? changeIsFemale.Value : defaults.IsFemale,
        Personality = Personality is { } changePersonality ? changePersonality.Value : defaults.Personality,
        BirthYear = BirthYear is { } changeBirthYear ? changeBirthYear.Value : defaults.BirthYear,
        LifespanTier = LifespanTier is { } changeLifespanTier ? changeLifespanTier.Value : defaults.LifespanTier,
        SuccessionPriority = SuccessionPriority is { } changeSuccessionPriority ? changeSuccessionPriority.Value : defaults.SuccessionPriority,
        AppearanceYear = AppearanceYear is { } changeAppearanceYear ? changeAppearanceYear.Value : defaults.AppearanceYear,
        BloodlineId = BloodlineId is { } changeBloodlineId ? changeBloodlineId.Value : defaults.BloodlineId,
        Surname = Surname is { } changeSurname ? changeSurname.Value : defaults.Surname,
        Righteousness = Righteousness is { } changeRighteousness ? changeRighteousness.Value : defaults.Righteousness,
        Ambition = Ambition is { } changeAmbition ? changeAmbition.Value : defaults.Ambition,
        ForceFounderPriority = ForceFounderPriority is { } changeForceFounderPriority ? changeForceFounderPriority.Value : defaults.ForceFounderPriority,
        PrefersConsolidationLeadership = PrefersConsolidationLeadership is { } changePrefersConsolidationLeadership ? changePrefersConsolidationLeadership.Value : defaults.PrefersConsolidationLeadership,
        AutomaticSurrenderAllowed = AutomaticSurrenderAllowed is { } changeAutomaticSurrenderAllowed ? changeAutomaticSurrenderAllowed.Value : defaults.AutomaticSurrenderAllowed,
        HasRecruitmentPoliticsBonus = HasRecruitmentPoliticsBonus is { } changeHasRecruitmentPoliticsBonus ? changeHasRecruitmentPoliticsBonus.Value : defaults.HasRecruitmentPoliticsBonus,
    };

    public static OfficerDefaultsOverrides Between(OfficerDefaults defaults, OfficerDefaults value) => new()
    {
        PortraitKey = ContentComparison.Equal(defaults.PortraitKey, value.PortraitKey) ? null : new(value.PortraitKey),
        NameKey = ContentComparison.Equal(defaults.NameKey, value.NameKey) ? null : new(value.NameKey),
        Name = ContentComparison.Equal(defaults.Name, value.Name) ? null : new(value.Name),
        CourtesyName = ContentComparison.Equal(defaults.CourtesyName, value.CourtesyName) ? null : new(value.CourtesyName),
        Biography = ContentComparison.Equal(defaults.Biography, value.Biography) ? null : new(value.Biography),
        AgeAtScenarioStart = ContentComparison.Equal(defaults.AgeAtScenarioStart, value.AgeAtScenarioStart) ? null : new(value.AgeAtScenarioStart),
        FlagGlyph = ContentComparison.Equal(defaults.FlagGlyph, value.FlagGlyph) ? null : new(value.FlagGlyph),
        CourtesyNameKey = ContentComparison.Equal(defaults.CourtesyNameKey, value.CourtesyNameKey) ? null : new(value.CourtesyNameKey),
        BiographyKey = ContentComparison.Equal(defaults.BiographyKey, value.BiographyKey) ? null : new(value.BiographyKey),
        War = ContentComparison.Equal(defaults.War, value.War) ? null : new(value.War),
        Intelligence = ContentComparison.Equal(defaults.Intelligence, value.Intelligence) ? null : new(value.Intelligence),
        Politics = ContentComparison.Equal(defaults.Politics, value.Politics) ? null : new(value.Politics),
        Leadership = ContentComparison.Equal(defaults.Leadership, value.Leadership) ? null : new(value.Leadership),
        Affinity = ContentComparison.Equal(defaults.Affinity, value.Affinity) ? null : new(value.Affinity),
        TacticLinkBonus = ContentComparison.Equal(defaults.TacticLinkBonus, value.TacticLinkBonus) ? null : new(value.TacticLinkBonus),
        DuelAdvantages = ContentComparison.Equal(defaults.DuelAdvantages, value.DuelAdvantages) ? null : new(value.DuelAdvantages),
        MasteredCategories = ContentComparison.Equal(defaults.MasteredCategories, value.MasteredCategories) ? null : new(value.MasteredCategories),
        LearnedTactics = ContentComparison.Equal(defaults.LearnedTactics, value.LearnedTactics) ? null : new(value.LearnedTactics),
        FormationProficiencies = ContentComparison.Equal(defaults.FormationProficiencies, value.FormationProficiencies) ? null : new(value.FormationProficiencies),
        TacticProficiencies = ContentComparison.Equal(defaults.TacticProficiencies, value.TacticProficiencies) ? null : new(value.TacticProficiencies),
        ParentId = ContentComparison.Equal(defaults.ParentId, value.ParentId) ? null : new(value.ParentId),
        IsFemale = ContentComparison.Equal(defaults.IsFemale, value.IsFemale) ? null : new(value.IsFemale),
        Personality = ContentComparison.Equal(defaults.Personality, value.Personality) ? null : new(value.Personality),
        BirthYear = ContentComparison.Equal(defaults.BirthYear, value.BirthYear) ? null : new(value.BirthYear),
        LifespanTier = ContentComparison.Equal(defaults.LifespanTier, value.LifespanTier) ? null : new(value.LifespanTier),
        SuccessionPriority = ContentComparison.Equal(defaults.SuccessionPriority, value.SuccessionPriority) ? null : new(value.SuccessionPriority),
        AppearanceYear = ContentComparison.Equal(defaults.AppearanceYear, value.AppearanceYear) ? null : new(value.AppearanceYear),
        BloodlineId = ContentComparison.Equal(defaults.BloodlineId, value.BloodlineId) ? null : new(value.BloodlineId),
        Surname = ContentComparison.Equal(defaults.Surname, value.Surname) ? null : new(value.Surname),
        Righteousness = ContentComparison.Equal(defaults.Righteousness, value.Righteousness) ? null : new(value.Righteousness),
        Ambition = ContentComparison.Equal(defaults.Ambition, value.Ambition) ? null : new(value.Ambition),
        ForceFounderPriority = ContentComparison.Equal(defaults.ForceFounderPriority, value.ForceFounderPriority) ? null : new(value.ForceFounderPriority),
        PrefersConsolidationLeadership = ContentComparison.Equal(defaults.PrefersConsolidationLeadership, value.PrefersConsolidationLeadership) ? null : new(value.PrefersConsolidationLeadership),
        AutomaticSurrenderAllowed = ContentComparison.Equal(defaults.AutomaticSurrenderAllowed, value.AutomaticSurrenderAllowed) ? null : new(value.AutomaticSurrenderAllowed),
        HasRecruitmentPoliticsBonus = ContentComparison.Equal(defaults.HasRecruitmentPoliticsBonus, value.HasRecruitmentPoliticsBonus) ? null : new(value.HasRecruitmentPoliticsBonus),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioOfficerState(int Id)
{
    [JsonRequired] public required int? OwnerId { get; init; }
    [JsonRequired] public required int? SettlementId { get; init; }
    [JsonRequired] public required bool Acted { get; init; }
    [JsonRequired] public required bool IsDormant { get; init; }
    [JsonRequired] public required int? UnitId { get; init; }
    [JsonRequired] public required int? EquippedTacticId { get; init; }
    [JsonRequired] public required int TacticCooldown { get; init; }
    [JsonRequired] public required int? StatusStartYear { get; init; }
    [JsonRequired] public required int Injury { get; init; }
    [JsonRequired] public required OfficerStatus Status { get; init; }
    [JsonRequired] public required int? RegionId { get; init; }
    [JsonRequired] public required int? OfficeId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PersonalPeerage? PersonalPeerage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ImmutableSortedDictionary<int, int>? BattleMerit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ImmutableSortedDictionary<int, int>? ServiceMerit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PersonalPeerageEvidence? InitialPersonalPeerage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PersonalPeerageEvidence[]? InitialPersonalPeerageHistory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormationEvidence[]? InitialFormationEvidence { get; init; }
    [JsonRequired] public required bool IsCommander { get; init; }
    [JsonRequired] public required ImmutableDictionary<int, int> ForceLoyalties { get; init; }
    [JsonRequired] public required bool RewardedThisXun { get; init; }
    [JsonRequired] public required OfficerDefaultsOverrides Overrides { get; init; }

    public static ScenarioOfficerState From(OfficerInitialState value, OfficerDefaults defaults, TextCatalog texts, int defaultLoyalty) => new(value.State.Id)
    {
        OwnerId = value.State.OwnerId,
        SettlementId = value.State.SettlementId,
        Acted = value.State.Acted,
        IsDormant = value.State.IsDormant,
        UnitId = value.State.UnitId,
        EquippedTacticId = value.State.EquippedTacticId,
        TacticCooldown = value.State.TacticCooldown,
        StatusStartYear = value.State.StatusStartYear,
        Injury = value.State.Injury,
        Status = value.State.Status,
        RegionId = value.State.RegionId,
        OfficeId = value.State.OfficeId,
        PersonalPeerage = value.State.PersonalPeerage,
        BattleMerit = value.State.BattleMerit,
        ServiceMerit = value.State.ServiceMerit,
        InitialPersonalPeerage = value.InitialPersonalPeerage,
        InitialPersonalPeerageHistory = value.InitialPersonalPeerageHistory,
        InitialFormationEvidence = value.InitialFormationEvidence,
        IsCommander = value.State.IsCommander,
        ForceLoyalties = value.State.WithLoyaltyDefault(defaultLoyalty).ForceLoyalties,
        RewardedThisXun = value.State.RewardedThisXun,
        Overrides = OfficerDefaultsOverrides.Between(defaults, OfficerDefaults.From(value, texts)),
    };

    public OfficerInitialState Compile(OfficerDefaults defaults)
    {
        var profile = Overrides.Apply(defaults);
        ContentStructure.Validate(profile);
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Officer name is required.");
        return new(profile.NameKey, new(Id, OwnerId, SettlementId, new(profile.War, profile.Intelligence, profile.Politics, profile.Leadership), Acted)
        {
            IsDormant = IsDormant,
            UnitId = UnitId,
            EquippedTacticId = EquippedTacticId,
            TacticCooldown = TacticCooldown,
            StatusStartYear = StatusStartYear,
            Injury = Injury,
            Status = Status,
            RegionId = RegionId,
            OfficeId = OfficeId,
            PersonalPeerage = PersonalPeerage,
            BattleMerit = BattleMerit,
            ServiceMerit = ServiceMerit,
            IsCommander = IsCommander,
            ForceLoyalties = ForceLoyalties,
            RewardedThisXun = RewardedThisXun,
            Affinity = profile.Affinity,
            TacticLinkBonus = profile.TacticLinkBonus,
            DuelAdvantages = profile.DuelAdvantages,
            MasteredCategories = profile.MasteredCategories,
            LearnedTactics = profile.LearnedTactics,
            FormationProficiencies = profile.FormationProficiencies,
            TacticProficiencies = profile.TacticProficiencies,
            ParentId = profile.ParentId,
            IsFemale = profile.IsFemale,
            Personality = profile.Personality,
            BirthYear = profile.BirthYear,
            LifespanTier = profile.LifespanTier,
            SuccessionPriority = profile.SuccessionPriority,
            AppearanceYear = profile.AppearanceYear,
            BloodlineId = profile.BloodlineId,
            Surname = profile.Surname,
            Righteousness = profile.Righteousness,
            Ambition = profile.Ambition,
            ForceFounderPriority = profile.ForceFounderPriority,
            PrefersConsolidationLeadership = profile.PrefersConsolidationLeadership,
            AutomaticSurrenderAllowed = profile.AutomaticSurrenderAllowed,
            HasRecruitmentPoliticsBonus = profile.HasRecruitmentPoliticsBonus,
        })
        {
            InitialPersonalPeerage = InitialPersonalPeerage,
            InitialPersonalPeerageHistory = InitialPersonalPeerageHistory,
            InitialFormationEvidence = InitialFormationEvidence,
            AgeAtScenarioStart = profile.AgeAtScenarioStart,
            FlagGlyph = profile.FlagGlyph,
            CourtesyNameKey = profile.CourtesyName is null ? null : profile.CourtesyNameKey ?? $"officer-courtesy.{Id}",
            BiographyKey = profile.Biography is null ? null : profile.BiographyKey ?? $"officer-biography.{Id}",
        };
    }
}

public static class OfficerComposition
{
    private static Dictionary<int, OfficerDefaults> Index(OfficerDefaults[] defaults)
    {
        if (defaults.Any(p => p.Id < 0 || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.NameKey)) || defaults.Select(p => p.Id).Distinct().Count() != defaults.Length)
            throw new InvalidDataException("Invalid or duplicate officer default identity.");
        return defaults.ToDictionary(p => p.Id);
    }

    public static ScenarioOfficerState[] Split(OfficerInitialState[] officers, OfficerDefaults[] defaults, TextCatalog texts, int defaultLoyalty)
    {
        var index = Index(defaults);
        return officers.Select(person => ScenarioOfficerState.From(person, index.TryGetValue(person.State.Id, out var profile)
            ? profile : throw new InvalidDataException($"Missing officer defaults: {person.State.Id}."), texts, defaultLoyalty)).ToArray();
    }

    public static OfficerDefaults[] Profiles(ScenarioOfficerState[] placements, OfficerDefaults[] defaults)
    {
        var index = Index(defaults);
        return placements.Select(p => p.Overrides.Apply(index.TryGetValue(p.Id, out var profile) ? profile
            : throw new InvalidDataException($"Unknown officer defaults: {p.Id}."))).ToArray();
    }

    public static TextCatalog ProjectText(TextCatalog texts, IEnumerable<OfficerDefaults> profiles)
    {
        var entries = new Dictionary<string, string>(texts.Entries, StringComparer.Ordinal);
        var people = new Dictionary<int, MessagePersonProfile>(texts.MessagePeople);
        foreach (var p in profiles)
        {
            entries[p.NameKey] = MessageMarkup.Escape(p.Name);
            if (p.CourtesyName is { } courtesy) entries[p.CourtesyNameKey ?? $"officer-courtesy.{p.Id}"] = MessageMarkup.Escape(courtesy);
            if (p.Biography is { } biography) entries[p.BiographyKey ?? $"officer-biography.{p.Id}"] = biography;
            string surname = $"officer-surname.{p.Id}", given = $"officer-given-name.{p.Id}";
            string givenText = p.GivenName;
            if (p.Surname.Length > 0) entries[surname] = MessageMarkup.Escape(p.Surname);
            if (givenText.Length > 0) entries[given] = MessageMarkup.Escape(givenText);
            people[p.Id] = new(p.Surname.Length > 0 ? surname : null, givenText.Length > 0 ? given : null,
                p.PortraitKey);
        }
        return texts with { Entries = entries, MessagePeople = people };
    }

    public static Dictionary<string, string> ProjectPortraits(IReadOnlyDictionary<string, string> source, IEnumerable<OfficerDefaults> profiles)
    {
        var roles = new Dictionary<string, string>(source);
        foreach (var p in profiles)
            foreach (string role in new[] { $"catalog-person-{p.Id}", $"information-person-{p.Id}" })
                if (p.PortraitKey is { } portrait) roles[role] = portrait;
                else roles.Remove(role);
        return roles;
    }

    public static OfficerInitialState[] Compile(ScenarioOfficerState[] placements, OfficerDefaults[] defaults)
    {
        var index = Index(defaults);
        if (placements.Select(p => p.Id).Distinct().Count() != placements.Length)
            throw new InvalidDataException("Duplicate scenario officer identity.");
        return placements.Select(person => person.Compile(index.TryGetValue(person.Id, out var profile)
            ? profile : throw new InvalidDataException($"Unknown officer defaults: {person.Id}."))).ToArray();
    }
}
