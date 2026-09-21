using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal sealed class ScenarioSessionDefinition
{
    internal ScenarioSessionDefinition(ScenarioInitialState definition)
    {
        definition.Validate();
        definition = InitializePlayerActivation(definition);
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        Content = definition with
        {
            Information = definition.Information.Copy(), Forces = definition.Forces.ToArray(),
            Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            FacilityNameSuffixKeys = new(definition.FacilityNameSuffixKeys), RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds),
            CityProvinceIds = new(definition.CityProvinceIds)
        };
        MilitaryRules = Content.Military is { } military ? new MilitaryRulebook(military) : null;
        Fingerprint = CanonicalJson.Hash(new
        {
            definition.SchemaVersion, definition.ScenarioId, definition.Year, definition.Month, definition.Day, definition.PlayerForceId, definition.DomesticPairs, definition.ImperialSealId, definition.ImperialCourt, definition.ImperialCourtEnded,
            definition.ShanyangGong,
            Guandu = definition.Guandu is { } guandu ? guandu with
                { PatronRegionIds = guandu.PatronRegionIds.Order().ToImmutableArray(), EnemyRegionIds = guandu.EnemyRegionIds.Order().ToImmutableArray() } : null,
            RedCliffs = definition.RedCliffs is { } battle ? battle with
                { ExcludedRulerIds = battle.ExcludedRulerIds.Order().ToImmutableArray(), RegionIds = battle.RegionIds.Order().ToImmutableArray() } : null,
            ChainedPlot = definition.ChainedPlot is { } plot ? plot with
                { SupporterIds = plot.SupporterIds.Order().ToImmutableArray(), OpponentIds = plot.OpponentIds.Order().ToImmutableArray() } : null,
            definition.Military, definition.ThreeVisitsStarted,
            ThreeVisits = definition.ThreeVisits is { } visits ? visits with { CompanionIds = visits.CompanionIds.Order().ToImmutableArray() } : null,
            definition.HanzhongKing, definition.FiveTigers, definition.ExpeditionMemorial, definition.SunMarriage, definition.PoliticalEvents, definition.ProvinceCountryTitleIds,
            QiaoMarriage = definition.QiaoMarriage is { } qiao ? qiao with
                { RequiredLivingOfficerIds = qiao.RequiredLivingOfficerIds.Order().ToImmutableArray(), ExcludedCityIds = qiao.ExcludedCityIds.Order().ToImmutableArray() } : null,
            CountryTitles = definition.Information.CountryTitles.OrderBy(t => t.Id).Select(t => t.Rule with
                { PreferredRulerIds = t.Rule.PreferredRulerIds.Order().ToImmutableArray(), PreferredBloodlineIds = t.Rule.PreferredBloodlineIds.Order().ToImmutableArray() }),
            Terminal = definition.Terminal is { } terminal ? new { Cities = terminal.CoreCityIds.Order(), terminal.EndYear } : null,
            Ending = definition.Ending is { } ending ? new
            {
                ExpansionOrder = ending.ExpansionPublicOrderCityIds.Order(), ExpansionGateways = ending.ExpansionGatewayCityIds.Order(),
                ExpansionCommerce = ending.ExpansionCommerceCityIds.Order(), DomesticDurability = ending.DomesticDurabilityCityIds.Order(),
                DomesticGateways = ending.DomesticGatewayCityIds.Order(), DomesticOrder = ending.DomesticPublicOrderCityIds.Order(),
                Treasures = ending.CulturalTreasureIds.Order()
            } : null,
            definition.Alliance,
            definition.AnnualAppearancesEnabled,
            definition.NaturalDeathsEnabled,
            definition.FoodExchangeRate, definition.FoodMarket,
            definition.FreeOfficerMigrationEnabled,
            definition.OfficeRequestsEnabled,
            definition.ForceConsolidationEnabled,
            definition.ForceEmergenceEnabled,
            definition.CityIndependenceEnabled,
            definition.UnitDefectionsEnabled,
            definition.SettlementDefectionsEnabled,
            TacticCourses = definition.TacticCourses.OrderBy(c => c.CityId),
            AttackRequests = definition.AttackRequests.OrderBy(r => r.RequestedForceId),
            ConsumedEventKeys = definition.ConsumedEventKeys.OrderBy(k => k.Value, StringComparer.Ordinal),
            SearchEvents = definition.SearchEvents.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e switch
            {
                HealingSearchEventDefinition healing => (SearchEventDefinition)(healing with
                    { ExcludedSearchingOfficerIds = healing.ExcludedSearchingOfficerIds.Order().ToImmutableArray() }),
                MerchantInvestmentDefinition merchant => merchant with { Categories = merchant.Categories.OrderBy(c => c.Id, StringComparer.Ordinal)
                    .Select(c => c with { PlaceIds = c.PlaceIds.Order().ToImmutableArray() }).ToImmutableArray() },
                MoneyDemandSearchEventDefinition demand => demand with { Months = demand.Months.Order().ToImmutableArray(), DemandingOfficerIds = demand.DemandingOfficerIds.Order().ToImmutableArray() },
                ScholarPunishmentSearchEventDefinition scholar => scholar with { Months = scholar.Months.Order().ToImmutableArray(), Sites = scholar.Sites.OrderBy(s => s.PlaceId).ToImmutableArray() },
                BanditMediationSearchEventDefinition mediation => mediation with { Months = mediation.Months.Order().ToImmutableArray(), Sites = mediation.Sites.OrderBy(s => s.PlaceId).ToImmutableArray(), GuaranteedOfficerIds = mediation.GuaranteedOfficerIds.Order().ToImmutableArray() },
                CommemorationSearchEventDefinition memorial => memorial with { Months = memorial.Months.Order().ToImmutableArray(), Sites = memorial.Sites.OrderBy(s => s.PlaceId).ToImmutableArray() },
                TrainingSiteSearchEventDefinition training => training with { Months = training.Months.Order().ToImmutableArray(), Sites = training.Sites.OrderBy(s => s.PlaceId).ToImmutableArray() },
                CommunitySearchEventDefinition community => community with { Months = community.Months.Order().ToImmutableArray() },
                ConflictSearchEventDefinition conflict => conflict with { Months = conflict.Months.Order().ToImmutableArray() },
                TigerSearchEventDefinition tiger => tiger with { ProvinceIds = tiger.ProvinceIds.Order().ToImmutableArray() },
                BeastSearchEventDefinition beast => beast with { Sites = beast.Sites.OrderBy(s => s.PlaceId).ToImmutableArray(),
                    BattleTacticIds = beast.BattleTacticIds.Order().ToImmutableArray() },
                ForceActivationSearchEventDefinition activation => activation with
                    { AttachedSettlementIds = activation.AttachedSettlementIds.Order().ToImmutableArray(), OfficerIds = activation.OfficerIds.Order().ToImmutableArray() },
                WomenVisitSearchEventDefinition visit => visit with { Sites = visit.Sites.OrderBy(s => s.PlaceId)
                    .Select(s => s with { RequiredLeaderIds = s.RequiredLeaderIds.Order().ToImmutableArray() }).ToImmutableArray() },
                SageVisitSearchEventDefinition sage => sage with { RecommendedOfficerIds = sage.RecommendedOfficerIds.Order().ToImmutableArray() },
                PhysicianVisitSearchEventDefinition physician => physician with { Sites = physician.Sites.OrderBy(s => s.PlaceId).ToImmutableArray() },
                _ => e
            }),
            Forces = Forces().OrderBy(f => f.Id), Settlements = Settlements().OrderBy(s => s.Id),
            Officers = definition.Officers.Select(o => o.State with { LearnedTactics = o.State.LearnedTactics.Order().ToImmutableArray() }).OrderBy(o => o.Id),
            Units = definition.Units.OrderBy(u => u.Id), Corps = definition.Information.Corps.OrderBy(c => c.Id),
            Treasures = definition.Information.TreasureStates.OrderBy(t => t.Id),
            Relations = definition.Information.Relations.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId),
            Coordinates = definition.Settlements.OrderBy(s => s.Id).Select(s => new { s.Id, s.X, s.Y }),
            UnbuiltPlaces = definition.UnbuiltPlaces.OrderBy(p => p.Id),
            Regions = definition.RegionNameKeys.Keys.Order(), definition.RegionCityIds, definition.CityProvinceIds,
            CityConnections = definition.CityConnections.OrderBy(c => c.FromCityId).ThenBy(c => c.ToCityId),
            Ranks = definition.Information.Ranks.OrderBy(r => r.Id).Select(r => new { r.Id, r.CommandCapacity, r.RequiredTrust, r.LoyaltyBonus, r.Precedence }),
            Offices = definition.Information.Offices.OrderBy(o => o.Id).Select(o => new { o.Id, o.CommandCapacity, o.Salary, o.GovernorPriority, o.AbilityBonuses, o.RequiredRankId }),
            TreasureDefinitions = definition.Information.Treasures.OrderBy(t => t.Id).Select(t => new { t.Id, t.Category, t.AbilityBonus, t.AbilityBonuses, t.LoyaltyBonus, t.TacticId, t.EscapeMount,
                t.RetentionGroup, t.AutomaticDiplomacyOfferAllowed, FavoredOwnerIds = t.FavoredOwnerIds.Order() })
        });
    }

    internal ScenarioInitialState Content { get; }

    private static ScenarioInitialState InitializePlayerActivation(ScenarioInitialState definition)
    {
        var activation = definition.SearchEvents.OfType<ForceActivationSearchEventDefinition>()
            .SingleOrDefault(d => d.ForceId == definition.PlayerForceId);
        if (activation is null || !definition.Forces.Any(f => f.Id == activation.ForceId && f.IsDormant)) return definition;
        // Selecting a reserved force starts with its authored resources, without the exploration reward.
        return definition with
        {
            Forces = definition.Forces.Select(f => f.Id == activation.ForceId ? f with { IsDormant = false } : f).ToArray(),
            Settlements = definition.Settlements.Select(s => s.Id == activation.CityId || activation.AttachedSettlementIds.Contains(s.Id)
                ? s with { IsDormant = false } : s).ToArray(),
            Officers = definition.Officers.Select(o => activation.OfficerIds.Contains(o.State.Id)
                ? o with { State = o.State with { IsDormant = false } } : o).ToArray(),
            ConsumedEventKeys = definition.ConsumedEventKeys.Append(activation.ConsumptionKey).Distinct()
                .OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray()
        };
    }
    internal MilitaryRulebook? MilitaryRules { get; }
    internal string Fingerprint { get; }
    internal WorldState CreateWorld(ulong seed, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false) => WorldState.Create(new(Content.Year, Content.Month, Content.Day), seed,
        Forces().Select(f => f.Id == Content.PlayerForceId ? f with { IsBarbarian = false } : f),
        Settlements(), Content.Officers.Select(o => o.State), Content.Units, Content.Information.Corps,
        Content.Information.TreasureStates, Content.Information.Relations,
        Content.Forces.Where(f => !f.IsDormant).OrderBy(f => f.Id == Content.PlayerForceId ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(f.Id == Content.PlayerForceId ? "player" : $"ai:{f.Id}", f.Id,
                f.Id == Content.PlayerForceId ? ControllerKind.Player : ControllerKind.Automatic)), computerDamageBonusPercent, Content.Alliance,
        Content.AttackRequests.Select(r => new AttackRequestState(r.RequestedForceId, r.RequesterForceId, r.TargetSettlementId,
            Content.Settlements.Single(s => s.Id == r.TargetSettlementId).OwnerId!.Value, r.RemainingXun)), Content.AnnualAppearancesEnabled, Content.NaturalDeathsEnabled, Content.FreeOfficerMigrationEnabled, battleDeathsEnabled, Content.OfficeRequestsEnabled, Content.ConsumedEventKeys, Content.ForceConsolidationEnabled, Content.ForceEmergenceEnabled, Content.CityIndependenceEnabled, Content.ImperialCourtEnded, Content.ThreeVisitsStarted, Content.FoodExchangeRate, Content.SettlementDefectionsEnabled, Content.UnitDefectionsEnabled);

    private IEnumerable<ForceState> Forces() => Content.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust)
    {
        ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray(), IsBarbarian = f.IsBarbarian, IsDormant = f.IsDormant,
        EventHistory = f.EventHistory.OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray(), HasDeposedEmperor = f.HasDeposedEmperor, LeaderId = f.LeaderId, AdviserId = f.AdviserId, RankId = f.RankId, CountryTitleId = f.CountryTitleId, BaseTrust = f.BaseTrust
    });

    private IEnumerable<SettlementState> Settlements() => Content.Settlements.Select(s => new SettlementState(s.Id,
        s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability, s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce,
        s.Cultivation, s.MaxCultivation)
    {
        MerchantPresenceChance = s.MerchantPresenceChance, IsDormant = s.IsDormant, Kind = s.Kind, CorpsId = s.CorpsId, CanHostPersons = s.CanHostPersons, SoldierCapacity = s.SoldierCapacity, ConsumesFood = s.ConsumesFood, Population = s.Population, DraftPopulation = s.DraftPopulation, Wounded = s.Wounded
    });
}
