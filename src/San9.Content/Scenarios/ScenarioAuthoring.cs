using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Content.Packages;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Content.Scenarios;

/// <summary>An explicit replacement; absence inherits, while a present null clears a nullable value.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContentOverride<T>(T Value);

/// <summary>Reusable catalogs and rules, independent of a scenario date or world state.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioDefinitions
{
    public const int CurrentSchemaVersion = 8;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public long NextFormationId { get; init; }
    [JsonRequired] public long NextTacticId { get; init; }
    [JsonRequired] public long NextOfficeId { get; init; }
    [JsonRequired] public long NextRankId { get; init; }
    [JsonRequired] public required OfficerGrowthRules OfficerGrowth { get; init; }
    [JsonRequired] public required OriginalAiDefinition OriginalAi { get; init; }
    [JsonRequired] public required int DefaultLoyalty { get; init; }
    [JsonRequired] public required int FoodExchangeRate { get; init; }
    [JsonRequired] public required FoodMarketDefinition? FoodMarket { get; init; }
    [JsonRequired] public required HanzhongKingDefinition? HanzhongKing { get; init; }
    [JsonRequired] public required FiveTigersDefinition? FiveTigers { get; init; }
    [JsonRequired] public required ExpeditionMemorialDefinition? ExpeditionMemorial { get; init; }
    [JsonRequired] public required SunMarriageDefinition? SunMarriage { get; init; }
    [JsonRequired] public required ShanyangGongDefinition? ShanyangGong { get; init; }
    [JsonRequired] public required RedCliffsDefinition? RedCliffs { get; init; }
    [JsonRequired] public required GuanduDefinition? Guandu { get; init; }
    [JsonRequired] public required ChainedPlotDefinition? ChainedPlot { get; init; }
    [JsonRequired] public required ThreeVisitsDefinition? ThreeVisits { get; init; }
    [JsonRequired] public required QiaoMarriageDefinition? QiaoMarriage { get; init; }
    [JsonRequired] public required PoliticalEventsDefinition? PoliticalEvents { get; init; }
    [JsonRequired] public required ImmutableDictionary<int, int> ProvinceCountryTitleIds { get; init; }
    [JsonRequired] public required ImmutableArray<SearchEventDefinition> SearchEvents { get; init; }
    [JsonRequired] public required TerminalDefinition? Terminal { get; init; }
    [JsonRequired] public required EndingDefinition? Ending { get; init; }
    [JsonRequired] public required ImmutableArray<CityTacticCourse> TacticCourses { get; init; }
    [JsonRequired] public required Dictionary<int, string> FacilityNameSuffixKeys { get; init; }
    [JsonRequired] public required ImmutableArray<DomesticPair> DomesticPairs { get; init; }
    [JsonRequired] public required int? ImperialSealId { get; init; }
    [JsonRequired] public required ImperialCourtDefinition ImperialCourt { get; init; }
    [JsonRequired] public required InformationCatalog Information { get; init; }
    [JsonRequired] public required MilitaryRulesDefinition? Military { get; init; }

    public static ScenarioDefinitions From(ScenarioInitialState scenario) => new()
    {
        NextFormationId = (long)(scenario.Military?.Formations.Select(f => f.Id).DefaultIfEmpty(-1).Max() ?? -1) + 1,
        NextTacticId = (long)(scenario.Military?.Tactics.Select(t => t.Id).DefaultIfEmpty(-1).Max() ?? -1) + 1,
        NextOfficeId = (long)scenario.Information.Offices.Select(o => o.Id).DefaultIfEmpty(-1).Max() + 1,
        NextRankId = (long)scenario.Information.Ranks.Select(r => r.Id).DefaultIfEmpty(-1).Max() + 1,
        OfficerGrowth = scenario.OfficerGrowth,
        OriginalAi = scenario.OriginalAi,
        DefaultLoyalty = scenario.DefaultLoyalty,
        FoodExchangeRate = scenario.FoodExchangeRate,
        FoodMarket = scenario.FoodMarket,
        HanzhongKing = scenario.HanzhongKing,
        FiveTigers = scenario.FiveTigers,
        ExpeditionMemorial = scenario.ExpeditionMemorial,
        SunMarriage = scenario.SunMarriage,
        ShanyangGong = scenario.ShanyangGong,
        RedCliffs = scenario.RedCliffs,
        Guandu = scenario.Guandu,
        ChainedPlot = scenario.ChainedPlot,
        ThreeVisits = scenario.ThreeVisits,
        QiaoMarriage = scenario.QiaoMarriage,
        PoliticalEvents = scenario.PoliticalEvents,
        ProvinceCountryTitleIds = scenario.ProvinceCountryTitleIds,
        SearchEvents = scenario.SearchEvents,
        Terminal = scenario.Terminal,
        Ending = scenario.Ending,
        TacticCourses = scenario.TacticCourses,
        FacilityNameSuffixKeys = scenario.FacilityNameSuffixKeys,
        DomesticPairs = scenario.DomesticPairs,
        ImperialSealId = scenario.ImperialSealId,
        ImperialCourt = scenario.ImperialCourt,
        Information = InformationCatalog.From(scenario.Information),
        Military = scenario.Military is { } military ? MilitaryRulesDefinition.From(military) : null,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioDefinitionsOverrides
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<OfficerGrowthRules>? OfficerGrowth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<OriginalAiDefinition>? OriginalAi { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int>? DefaultLoyalty { get; init; }
    public ContentOverride<int>? FoodExchangeRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<FoodMarketDefinition?>? FoodMarket { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<HanzhongKingDefinition?>? HanzhongKing { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<FiveTigersDefinition?>? FiveTigers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ExpeditionMemorialDefinition?>? ExpeditionMemorial { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<SunMarriageDefinition?>? SunMarriage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ShanyangGongDefinition?>? ShanyangGong { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<RedCliffsDefinition?>? RedCliffs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<GuanduDefinition?>? Guandu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ChainedPlotDefinition?>? ChainedPlot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ThreeVisitsDefinition?>? ThreeVisits { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<QiaoMarriageDefinition?>? QiaoMarriage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<PoliticalEventsDefinition?>? PoliticalEvents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableDictionary<int, int>>? ProvinceCountryTitleIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableArray<SearchEventDefinition>>? SearchEvents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<TerminalDefinition?>? Terminal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<EndingDefinition?>? Ending { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableArray<CityTacticCourse>>? TacticCourses { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<Dictionary<int, string>>? FacilityNameSuffixKeys { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImmutableArray<DomesticPair>>? DomesticPairs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<int?>? ImperialSealId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<ImperialCourtDefinition>? ImperialCourt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<InformationCatalog>? Information { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentOverride<MilitaryRulesDefinition?>? Military { get; init; }

    public ScenarioDefinitions Apply(ScenarioDefinitions defaults) => defaults with
    {
        OfficerGrowth = OfficerGrowth is { } changeOfficerGrowth ? changeOfficerGrowth.Value : defaults.OfficerGrowth,
        OriginalAi = OriginalAi is { } changeOriginalAi ? changeOriginalAi.Value : defaults.OriginalAi,
        DefaultLoyalty = DefaultLoyalty is { } changeDefaultLoyalty ? changeDefaultLoyalty.Value : defaults.DefaultLoyalty,
        FoodExchangeRate = FoodExchangeRate is { } changeFoodExchangeRate ? changeFoodExchangeRate.Value : defaults.FoodExchangeRate,
        FoodMarket = FoodMarket is { } changeFoodMarket ? changeFoodMarket.Value : defaults.FoodMarket,
        HanzhongKing = HanzhongKing is { } changeHanzhongKing ? changeHanzhongKing.Value : defaults.HanzhongKing,
        FiveTigers = FiveTigers is { } changeFiveTigers ? changeFiveTigers.Value : defaults.FiveTigers,
        ExpeditionMemorial = ExpeditionMemorial is { } changeExpeditionMemorial ? changeExpeditionMemorial.Value : defaults.ExpeditionMemorial,
        SunMarriage = SunMarriage is { } changeSunMarriage ? changeSunMarriage.Value : defaults.SunMarriage,
        ShanyangGong = ShanyangGong is { } changeShanyangGong ? changeShanyangGong.Value : defaults.ShanyangGong,
        RedCliffs = RedCliffs is { } changeRedCliffs ? changeRedCliffs.Value : defaults.RedCliffs,
        Guandu = Guandu is { } changeGuandu ? changeGuandu.Value : defaults.Guandu,
        ChainedPlot = ChainedPlot is { } changeChainedPlot ? changeChainedPlot.Value : defaults.ChainedPlot,
        ThreeVisits = ThreeVisits is { } changeThreeVisits ? changeThreeVisits.Value : defaults.ThreeVisits,
        QiaoMarriage = QiaoMarriage is { } changeQiaoMarriage ? changeQiaoMarriage.Value : defaults.QiaoMarriage,
        PoliticalEvents = PoliticalEvents is { } changePoliticalEvents ? changePoliticalEvents.Value : defaults.PoliticalEvents,
        ProvinceCountryTitleIds = ProvinceCountryTitleIds is { } changeProvinceCountryTitleIds ? changeProvinceCountryTitleIds.Value : defaults.ProvinceCountryTitleIds,
        SearchEvents = SearchEvents is { } changeSearchEvents ? changeSearchEvents.Value : defaults.SearchEvents,
        Terminal = Terminal is { } changeTerminal ? changeTerminal.Value : defaults.Terminal,
        Ending = Ending is { } changeEnding ? changeEnding.Value : defaults.Ending,
        TacticCourses = TacticCourses is { } changeTacticCourses ? changeTacticCourses.Value : defaults.TacticCourses,
        FacilityNameSuffixKeys = FacilityNameSuffixKeys is { } changeFacilityNameSuffixKeys ? changeFacilityNameSuffixKeys.Value : defaults.FacilityNameSuffixKeys,
        DomesticPairs = DomesticPairs is { } changeDomesticPairs ? changeDomesticPairs.Value : defaults.DomesticPairs,
        ImperialSealId = ImperialSealId is { } changeImperialSealId ? changeImperialSealId.Value : defaults.ImperialSealId,
        ImperialCourt = ImperialCourt is { } changeImperialCourt ? changeImperialCourt.Value : defaults.ImperialCourt,
        Information = Information is { } changeInformation ? changeInformation.Value : defaults.Information,
        Military = Military is { } changeMilitary ? changeMilitary.Value : defaults.Military,
    };

    public static ScenarioDefinitionsOverrides Between(ScenarioDefinitions defaults, ScenarioDefinitions value) => new()
    {
        OfficerGrowth = ContentComparison.Equal(defaults.OfficerGrowth, value.OfficerGrowth) ? null : new(value.OfficerGrowth),
        OriginalAi = ContentComparison.Equal(defaults.OriginalAi, value.OriginalAi) ? null : new(value.OriginalAi),
        DefaultLoyalty = ContentComparison.Equal(defaults.DefaultLoyalty, value.DefaultLoyalty) ? null : new(value.DefaultLoyalty),
        FoodExchangeRate = ContentComparison.Equal(defaults.FoodExchangeRate, value.FoodExchangeRate) ? null : new(value.FoodExchangeRate),
        FoodMarket = ContentComparison.Equal(defaults.FoodMarket, value.FoodMarket) ? null : new(value.FoodMarket),
        HanzhongKing = ContentComparison.Equal(defaults.HanzhongKing, value.HanzhongKing) ? null : new(value.HanzhongKing),
        FiveTigers = ContentComparison.Equal(defaults.FiveTigers, value.FiveTigers) ? null : new(value.FiveTigers),
        ExpeditionMemorial = ContentComparison.Equal(defaults.ExpeditionMemorial, value.ExpeditionMemorial) ? null : new(value.ExpeditionMemorial),
        SunMarriage = ContentComparison.Equal(defaults.SunMarriage, value.SunMarriage) ? null : new(value.SunMarriage),
        ShanyangGong = ContentComparison.Equal(defaults.ShanyangGong, value.ShanyangGong) ? null : new(value.ShanyangGong),
        RedCliffs = ContentComparison.Equal(defaults.RedCliffs, value.RedCliffs) ? null : new(value.RedCliffs),
        Guandu = ContentComparison.Equal(defaults.Guandu, value.Guandu) ? null : new(value.Guandu),
        ChainedPlot = ContentComparison.Equal(defaults.ChainedPlot, value.ChainedPlot) ? null : new(value.ChainedPlot),
        ThreeVisits = ContentComparison.Equal(defaults.ThreeVisits, value.ThreeVisits) ? null : new(value.ThreeVisits),
        QiaoMarriage = ContentComparison.Equal(defaults.QiaoMarriage, value.QiaoMarriage) ? null : new(value.QiaoMarriage),
        PoliticalEvents = ContentComparison.Equal(defaults.PoliticalEvents, value.PoliticalEvents) ? null : new(value.PoliticalEvents),
        ProvinceCountryTitleIds = ContentComparison.Equal(defaults.ProvinceCountryTitleIds, value.ProvinceCountryTitleIds) ? null : new(value.ProvinceCountryTitleIds),
        SearchEvents = ContentComparison.Equal(defaults.SearchEvents, value.SearchEvents) ? null : new(value.SearchEvents),
        Terminal = ContentComparison.Equal(defaults.Terminal, value.Terminal) ? null : new(value.Terminal),
        Ending = ContentComparison.Equal(defaults.Ending, value.Ending) ? null : new(value.Ending),
        TacticCourses = ContentComparison.Equal(defaults.TacticCourses, value.TacticCourses) ? null : new(value.TacticCourses),
        FacilityNameSuffixKeys = ContentComparison.Equal(defaults.FacilityNameSuffixKeys, value.FacilityNameSuffixKeys) ? null : new(value.FacilityNameSuffixKeys),
        DomesticPairs = ContentComparison.Equal(defaults.DomesticPairs, value.DomesticPairs) ? null : new(value.DomesticPairs),
        ImperialSealId = ContentComparison.Equal(defaults.ImperialSealId, value.ImperialSealId) ? null : new(value.ImperialSealId),
        ImperialCourt = ContentComparison.Equal(defaults.ImperialCourt, value.ImperialCourt) ? null : new(value.ImperialCourt),
        Information = ContentComparison.Equal(defaults.Information, value.Information) ? null : new(value.Information),
        Military = ContentComparison.Equal(defaults.Military, value.Military) ? null : new(value.Military),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InformationCatalog
{
    [JsonRequired] public required RankDefinition[] Ranks { get; init; }
    [JsonRequired] public required OfficeDefinition[] Offices { get; init; }
    [JsonRequired] public required TreasureDefinition[] Treasures { get; init; }
    [JsonRequired] public required ImmutableArray<int> MapContextExcludedFacilityIds { get; init; }
    [JsonRequired] public required TacticCatalogDefinition[] Tactics { get; init; }
    [JsonRequired] public required FormationCatalogDefinition[] Formations { get; init; }
    [JsonRequired] public required CountryTitleDefinition[] CountryTitles { get; init; }

    public static InformationCatalog From(InformationDefinitions value) => new()
    {
        Ranks = value.Ranks,
        Offices = value.Offices,
        Treasures = value.Treasures,
        MapContextExcludedFacilityIds = value.MapContextExcludedFacilityIds,
        Tactics = value.Tactics,
        Formations = value.Formations,
        CountryTitles = value.CountryTitles,
    };

    public InformationDefinitions Compile(ScenarioInformationState state) => new(Ranks.ToArray(), Offices.ToArray(), Treasures.ToArray(), state.Corps, state.TreasureStates)
    {
        MapContextExcludedFacilityIds = MapContextExcludedFacilityIds,
        Tactics = Tactics,
        Formations = Formations,
        CountryTitles = CountryTitles,
        ShowOfficerAges = state.ShowOfficerAges, Relations = state.Relations,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioInformationState(CorpsState[] Corps, TreasureState[] TreasureStates, ForceRelation[] Relations, bool ShowOfficerAges)
{
    public static ScenarioInformationState From(InformationDefinitions value) => new(value.Corps, value.TreasureStates, value.Relations, value.ShowOfficerAges);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MilitaryRulesDefinition
{
    [JsonRequired] public required ImmutableArray<TerrainPassage> Terrains { get; init; }
    [JsonRequired] public required ImmutableArray<FormationRule> Formations { get; init; }
    [JsonRequired] public required CompanionCombatDefinition? CompanionCombat { get; init; }
    [JsonRequired] public required CompanionTacticPresets? CompanionTactics { get; init; }
    [JsonRequired] public required FixedUnitFormations? ConstructionFormations { get; init; }
    [JsonRequired] public required FixedUnitFormations? TransportFormations { get; init; }
    [JsonRequired] public required int? BasicNavalFormationId { get; init; }
    [JsonRequired] public required int? DefaultDeploymentFormationId { get; init; }
    [JsonRequired] public required int? UnformedFormationId { get; init; }
    [JsonRequired] public required int DefensiveTacticChance { get; init; }
    [JsonRequired] public required bool BattleDeathsAllowed { get; init; }
    [JsonRequired] public required bool DuelCapturesAllowed { get; init; }
    [JsonRequired] public required int PolicyRadius { get; init; }
    [JsonRequired] public required ImmutableArray<SoldierVisionBonus> VisionSizeBands { get; init; }
    [JsonRequired] public required ImmutableArray<FacilityRule> Facilities { get; init; }
    [JsonRequired] public required ImmutableArray<TrapTerrainRule> TrapTerrains { get; init; }
    [JsonRequired] public required HigherGroundTrapRule? HigherGroundTrap { get; init; }
    [JsonRequired] public required ImmutableArray<TacticRule> Tactics { get; init; }
    [JsonRequired] public required TacticLinkRules LinkRules { get; init; }
    [JsonRequired] public required CityInfluenceRule? CityInfluence { get; init; }
    [JsonRequired] public required RockfallRule? Rockfall { get; init; }

    public static MilitaryRulesDefinition From(MilitaryDefinition value) => new()
    {
        Terrains = value.Terrains,
        Formations = value.Formations,
        CompanionCombat = value.CompanionCombat,
        CompanionTactics = value.CompanionTactics,
        ConstructionFormations = value.ConstructionFormations,
        TransportFormations = value.TransportFormations,
        BasicNavalFormationId = value.BasicNavalFormationId,
        DefaultDeploymentFormationId = value.DefaultDeploymentFormationId,
        UnformedFormationId = value.UnformedFormationId,
        DefensiveTacticChance = value.DefensiveTacticChance,
        BattleDeathsAllowed = value.BattleDeathsAllowed,
        DuelCapturesAllowed = value.DuelCapturesAllowed,
        PolicyRadius = value.PolicyRadius,
        VisionSizeBands = value.VisionSizeBands,
        Facilities = value.Facilities,
        TrapTerrains = value.TrapTerrains,
        HigherGroundTrap = value.HigherGroundTrap,
        Tactics = value.Tactics,
        LinkRules = value.LinkRules,
        CityInfluence = value.CityInfluence,
        Rockfall = value.Rockfall,
    };

    public MilitaryDefinition Compile(MilitaryGeographyDefinition geography, ImmutableArray<BattlefieldPlace> places) =>
        new(geography.Width, geography.Height, geography.TerrainIds, Terrains, Formations, places)
        {
            CompanionCombat = CompanionCombat,
            CompanionTactics = CompanionTactics,
            ConstructionFormations = ConstructionFormations,
            TransportFormations = TransportFormations,
            BasicNavalFormationId = BasicNavalFormationId,
            DefaultDeploymentFormationId = DefaultDeploymentFormationId,
            UnformedFormationId = UnformedFormationId,
            DefensiveTacticChance = DefensiveTacticChance,
            BattleDeathsAllowed = BattleDeathsAllowed,
            DuelCapturesAllowed = DuelCapturesAllowed,
            PolicyRadius = PolicyRadius,
            VisionSizeBands = VisionSizeBands,
            Facilities = Facilities,
            TrapTerrains = TrapTerrains,
            HigherGroundTrap = HigherGroundTrap,
            Tactics = Tactics,
            LinkRules = LinkRules,
            CityInfluence = CityInfluence,
            Rockfall = Rockfall,
            ConstructionSites = geography.ConstructionSites,
            Elevations = geography.Elevations,
            RegionIds = geography.RegionIds,
            TerritoryExits = geography.TerritoryExits,
            Geography = geography.Geography,
            BlockedCells = geography.BlockedCells,
        };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MilitaryGeographyDefinition
{
    [JsonRequired] public required int Width { get; init; }
    [JsonRequired] public required int Height { get; init; }
    [JsonRequired] public required ImmutableArray<int> TerrainIds { get; init; }
    [JsonRequired] public required ImmutableArray<ConstructionSite> ConstructionSites { get; init; }
    [JsonRequired] public required ImmutableArray<int> Elevations { get; init; }
    [JsonRequired] public required ImmutableArray<int?> RegionIds { get; init; }
    [JsonRequired] public required ImmutableArray<TerritoryExits> TerritoryExits { get; init; }
    [JsonRequired] public required BattlefieldGeography Geography { get; init; }
    [JsonRequired] public required ImmutableArray<MapCell> BlockedCells { get; init; }

    public static MilitaryGeographyDefinition From(MilitaryDefinition value) => new()
    {
        Width = value.Width,
        Height = value.Height,
        TerrainIds = value.TerrainIds,
        ConstructionSites = value.ConstructionSites,
        Elevations = value.Elevations,
        RegionIds = value.RegionIds,
        TerritoryExits = value.TerritoryExits,
        Geography = value.Geography,
        BlockedCells = value.BlockedCells,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioGeography
{
    public const int CurrentSchemaVersion = 2;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public required Dictionary<int, string> RegionNameKeys { get; init; }
    [JsonRequired] public required Dictionary<int, int> RegionCityIds { get; init; }
    [JsonRequired] public required int[] FictionalOfficerPlaceIds { get; init; }
    [JsonRequired] public required Dictionary<int, string> ProvinceNameKeys { get; init; }
    [JsonRequired] public required Dictionary<int, int> CityProvinceIds { get; init; }
    [JsonRequired] public required ImmutableArray<CityConnection> CityConnections { get; init; }
    [JsonRequired] public required MilitaryGeographyDefinition? Military { get; init; }

    public static ScenarioGeography From(ScenarioInitialState value) => new()
    {
        RegionNameKeys = value.RegionNameKeys,
        RegionCityIds = value.RegionCityIds,
        FictionalOfficerPlaceIds = value.FictionalOfficerPlaceIds,
        ProvinceNameKeys = value.ProvinceNameKeys,
        CityProvinceIds = value.CityProvinceIds,
        CityConnections = value.CityConnections,
        Military = value.Military is { } military ? MilitaryGeographyDefinition.From(military) : null,
    };
}

/// <summary>Authored scenario state and explicit differences; definitions are resolved before creating a world.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioDocument(int SchemaVersion, string ScenarioId, int Year, int Month, int Day, int? PlayerForceId,
    ForceInitialState[] Forces, SettlementInitialState[] Settlements)
{
    public const int CurrentSchemaVersion = 6;
    [JsonRequired] public required GameClock? ThreeVisitsStarted { get; init; }
    [JsonRequired] public required bool UnitDefectionsEnabled { get; init; }
    [JsonRequired] public required bool SettlementDefectionsEnabled { get; init; }
    [JsonRequired] public required bool CityIndependenceEnabled { get; init; }
    [JsonRequired] public required bool ForceEmergenceEnabled { get; init; }
    [JsonRequired] public required bool ForceConsolidationEnabled { get; init; }
    [JsonRequired] public required ImmutableArray<PersistentEventKey> ConsumedEventKeys { get; init; }
    [JsonRequired] public required bool OfficeRequestsEnabled { get; init; }
    [JsonRequired] public required bool FreeOfficerMigrationEnabled { get; init; }
    [JsonRequired] public required bool AnnualAppearancesEnabled { get; init; }
    [JsonRequired] public required bool NaturalDeathsEnabled { get; init; }
    [JsonRequired] public required ImmutableArray<AttackRequestInitialState> AttackRequests { get; init; }
    [JsonRequired] public required AllianceState? Alliance { get; init; }
    [JsonRequired] public required ImmutableArray<PersonPlace> UnbuiltPlaces { get; init; }
    [JsonRequired] public required bool ImperialCourtEnded { get; init; }
    [JsonRequired] public required UnitState[] Units { get; init; }
    [JsonRequired] public required OfficerDefaults[] AdditionalOfficerDefaults { get; init; }
    [JsonRequired] public required ScenarioOfficerState[] Officers { get; init; }
    [JsonRequired] public required ScenarioInformationState Information { get; init; }
    [JsonRequired] public required ImmutableArray<BattlefieldPlace> MilitaryPlaces { get; init; }
    [JsonRequired] public required ScenarioDefinitionsOverrides DefinitionOverrides { get; init; }

    public static ScenarioDocument From(ScenarioInitialState value, ScenarioDefinitions definitions, OfficerDefaults[] officers, San9.Content.Text.TextCatalog texts)
    {
        var known = officers.Select(p => p.Id).ToHashSet();
        var additional = value.Officers.Where(p => !known.Contains(p.State.Id)).Select(p => OfficerDefaults.From(p, texts)).ToArray();
        return new(CurrentSchemaVersion, value.ScenarioId, value.Year, value.Month, value.Day, value.PlayerForceId, value.Forces, value.Settlements)
        {
            ThreeVisitsStarted = value.ThreeVisitsStarted,
            UnitDefectionsEnabled = value.UnitDefectionsEnabled,
            SettlementDefectionsEnabled = value.SettlementDefectionsEnabled,
            CityIndependenceEnabled = value.CityIndependenceEnabled,
            ForceEmergenceEnabled = value.ForceEmergenceEnabled,
            ForceConsolidationEnabled = value.ForceConsolidationEnabled,
            ConsumedEventKeys = value.ConsumedEventKeys,
            OfficeRequestsEnabled = value.OfficeRequestsEnabled,
            FreeOfficerMigrationEnabled = value.FreeOfficerMigrationEnabled,
            AnnualAppearancesEnabled = value.AnnualAppearancesEnabled,
            NaturalDeathsEnabled = value.NaturalDeathsEnabled,
            AttackRequests = value.AttackRequests,
            Alliance = value.Alliance,
            UnbuiltPlaces = value.UnbuiltPlaces,
            ImperialCourtEnded = value.ImperialCourtEnded,
            Units = value.Units,
            AdditionalOfficerDefaults = additional,
            Officers = OfficerComposition.Split(value.Officers, [.. officers, .. additional], texts, value.DefaultLoyalty),
            Information = ScenarioInformationState.From(value.Information),
            MilitaryPlaces = value.Military?.Places ?? [],
            DefinitionOverrides = ScenarioDefinitionsOverrides.Between(definitions, ScenarioDefinitions.From(value)),
        };
    }

    public ScenarioInitialState Compile(ScenarioDefinitions defaults, OfficerDefaults[] officers, ScenarioGeography geography)
    {
        if (SchemaVersion != CurrentSchemaVersion || defaults.SchemaVersion != ScenarioDefinitions.CurrentSchemaVersion || geography.SchemaVersion != ScenarioGeography.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported authored scenario data version; reconvert the package.");
        ContentStructure.Validate(this);
        var definitions = DefinitionOverrides.Apply(defaults);
        ContentStructure.Validate(definitions);
        if ((definitions.Military is null) != (geography.Military is null) || definitions.Military is null && !MilitaryPlaces.IsEmpty)
            throw new InvalidDataException("Military rules, geography and placements must be declared together.");
        return new(ScenarioInitialState.CurrentSchemaVersion, ScenarioId, Year, Month, Day, PlayerForceId, Forces, Settlements,
            new(geography.RegionNameKeys), new(geography.RegionCityIds))
        {
            ThreeVisitsStarted = ThreeVisitsStarted,
            UnitDefectionsEnabled = UnitDefectionsEnabled,
            SettlementDefectionsEnabled = SettlementDefectionsEnabled,
            CityIndependenceEnabled = CityIndependenceEnabled,
            ForceEmergenceEnabled = ForceEmergenceEnabled,
            ForceConsolidationEnabled = ForceConsolidationEnabled,
            ConsumedEventKeys = ConsumedEventKeys,
            OfficeRequestsEnabled = OfficeRequestsEnabled,
            FreeOfficerMigrationEnabled = FreeOfficerMigrationEnabled,
            AnnualAppearancesEnabled = AnnualAppearancesEnabled,
            NaturalDeathsEnabled = NaturalDeathsEnabled,
            AttackRequests = AttackRequests,
            Alliance = Alliance,
            UnbuiltPlaces = UnbuiltPlaces,
            ImperialCourtEnded = ImperialCourtEnded,
            Units = Units,
            OfficerGrowth = definitions.OfficerGrowth,
            OriginalAi = definitions.OriginalAi,
            DefaultLoyalty = definitions.DefaultLoyalty,
            FoodExchangeRate = definitions.FoodExchangeRate,
            FoodMarket = definitions.FoodMarket,
            HanzhongKing = definitions.HanzhongKing,
            FiveTigers = definitions.FiveTigers,
            ExpeditionMemorial = definitions.ExpeditionMemorial,
            SunMarriage = definitions.SunMarriage,
            ShanyangGong = definitions.ShanyangGong,
            RedCliffs = definitions.RedCliffs,
            Guandu = definitions.Guandu,
            ChainedPlot = definitions.ChainedPlot,
            ThreeVisits = definitions.ThreeVisits,
            QiaoMarriage = definitions.QiaoMarriage,
            PoliticalEvents = definitions.PoliticalEvents,
            ProvinceCountryTitleIds = definitions.ProvinceCountryTitleIds,
            SearchEvents = definitions.SearchEvents,
            Terminal = definitions.Terminal,
            Ending = definitions.Ending,
            TacticCourses = definitions.TacticCourses,
            FacilityNameSuffixKeys = definitions.FacilityNameSuffixKeys,
            DomesticPairs = definitions.DomesticPairs,
            ImperialSealId = definitions.ImperialSealId,
            ImperialCourt = definitions.ImperialCourt,
            FictionalOfficerPlaceIds = geography.FictionalOfficerPlaceIds,
            ProvinceNameKeys = geography.ProvinceNameKeys,
            CityProvinceIds = geography.CityProvinceIds,
            CityConnections = geography.CityConnections,
            Officers = OfficerComposition.Compile(Officers, [.. officers, .. AdditionalOfficerDefaults]),
            Information = definitions.Information.Compile(Information),
            Military = definitions.Military?.Compile(geography.Military!, MilitaryPlaces),
        };
    }
}
