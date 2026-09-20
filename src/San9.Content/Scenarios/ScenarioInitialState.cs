using System.Text.Json.Serialization;

namespace San9.Content.Scenarios;

public sealed record ForceInitialState(int Id, string NameKey, int ColorCode, uint Money, uint Food, int Trust)
{
    [JsonRequired] public bool IsDormant { get; init; }
    [JsonRequired] public bool HasDeposedEmperor { get; init; }
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Simulation.PersistentEventKey> EventHistory { get; init; } = [];
    [JsonRequired] public int BaseTrust { get; init; }
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<int> ProvidedTactics { get; init; } = [];
    [JsonRequired] public bool IsBarbarian { get; init; }
    [JsonRequired] public int FlagGlyph { get; init; }
    [JsonRequired] public int? LeaderId { get; init; }
    [JsonRequired] public int? AdviserId { get; init; }
    [JsonRequired] public int? RankId { get; init; }
    [JsonRequired] public int? CountryTitleId { get; init; }
}
public sealed record SettlementInitialState(int Id, string NameKey, int? OwnerId, int X, int Y,
    uint Soldiers, int Durability, int MaxDurability, int Morale, string[] Icons)
{
    [JsonRequired] public bool IsDormant { get; init; }
    [JsonRequired] public uint Wounded { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public San9.Core.Domain.MapCell Cell => new(X, Y);
    [System.Text.Json.Serialization.JsonIgnore] public San9.Core.Domain.WorldPosition Position => San9.Core.Domain.MapCoordinates.Center(Cell);
    [JsonRequired] public uint SoldierCapacity { get; init; } = 500000;
    [JsonRequired] public bool ConsumesFood { get; init; } = true;
    [JsonRequired] public bool CanHostPersons { get; init; } = true;
    [JsonRequired] public San9.Core.Domain.SettlementKind Kind { get; init; }
    [JsonRequired] public int Appearance { get; init; }
    [JsonRequired] public int MerchantPresenceChance { get; init; }
    [JsonRequired] public int? CorpsId { get; init; }
    [JsonRequired] public bool IsFacilityListEntry { get; init; }
    [JsonRequired] public int PublicOrder { get; init; }
    [JsonRequired] public int Commerce { get; init; }
    [JsonRequired] public int MaxCommerce { get; init; }
    [JsonRequired] public int Cultivation { get; init; }
    [JsonRequired] public int MaxCultivation { get; init; }
    [JsonRequired] public uint Population { get; init; }
    [JsonRequired] public uint DraftPopulation { get; init; }
}
public sealed record ScenarioInitialState(int SchemaVersion, string ScenarioId, int Year, int Month, int Day, int PlayerForceId,
    ForceInitialState[] Forces, SettlementInitialState[] Settlements, Dictionary<int, string> RegionNameKeys, Dictionary<int, int> RegionCityIds)
{
    public const int CurrentSchemaVersion = 98;
    [JsonRequired] public int FoodExchangeRate { get; init; } = 1;
    [JsonRequired] public San9.Core.Domain.FoodMarketDefinition? FoodMarket { get; init; }
    [JsonRequired] public San9.Core.Domain.HanzhongKingDefinition? HanzhongKing { get; init; }
    [JsonRequired] public San9.Core.Domain.FiveTigersDefinition? FiveTigers { get; init; }
    [JsonRequired] public San9.Core.Domain.ExpeditionMemorialDefinition? ExpeditionMemorial { get; init; }
    [JsonRequired] public San9.Core.Domain.SunMarriageDefinition? SunMarriage { get; init; }
    [JsonRequired] public San9.Core.Domain.ShanyangGongDefinition? ShanyangGong { get; init; }
    [JsonRequired] public San9.Core.Domain.RedCliffsDefinition? RedCliffs { get; init; }
    [JsonRequired] public San9.Core.Domain.GuanduDefinition? Guandu { get; init; }
    [JsonRequired] public San9.Core.Domain.ChainedPlotDefinition? ChainedPlot { get; init; }
    [JsonRequired] public San9.Core.Domain.ThreeVisitsDefinition? ThreeVisits { get; init; }
    [JsonRequired] public San9.Core.Simulation.GameClock? ThreeVisitsStarted { get; init; }
    [JsonRequired] public San9.Core.Domain.QiaoMarriageDefinition? QiaoMarriage { get; init; }
    [JsonRequired] public San9.Core.Domain.PoliticalEventsDefinition? PoliticalEvents { get; init; }
    [JsonRequired] public System.Collections.Immutable.ImmutableDictionary<int, int> ProvinceCountryTitleIds { get; init; } = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty;
    [JsonRequired] public bool CityIndependenceEnabled { get; init; } = true;
    [JsonRequired] public bool ForceEmergenceEnabled { get; init; } = true;
    [JsonRequired] public bool ForceConsolidationEnabled { get; init; } = true;
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Simulation.PersistentEventKey> ConsumedEventKeys { get; init; } = [];
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.SearchEventDefinition> SearchEvents { get; init; } = [];
    [JsonRequired] public San9.Core.Domain.TerminalDefinition? Terminal { get; init; }
    [JsonRequired] public San9.Core.Domain.EndingDefinition? Ending { get; init; }
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.CityTacticCourse> TacticCourses { get; init; } = [];
    [JsonRequired] public bool OfficeRequestsEnabled { get; init; } = true;
    [JsonRequired] public bool FreeOfficerMigrationEnabled { get; init; } = true;
    [JsonRequired] public bool AnnualAppearancesEnabled { get; init; } = true;
    [JsonRequired] public bool NaturalDeathsEnabled { get; init; } = true;
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<AttackRequestInitialState> AttackRequests { get; init; } = [];
    [JsonRequired] public Dictionary<int, int> CityProvinceIds { get; init; } = [];
    [JsonRequired] public San9.Core.Domain.AllianceState? Alliance { get; init; }
    [JsonRequired] public Dictionary<int, string> FacilityNameSuffixKeys { get; init; } = [];
    [JsonRequired] public San9.Core.Domain.MilitaryDefinition? Military { get; init; }
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.CityConnection> CityConnections { get; init; } = [];
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.PersonPlace> UnbuiltPlaces { get; init; } = [];
    [JsonRequired] public System.Collections.Immutable.ImmutableArray<San9.Core.Domain.DomesticPair> DomesticPairs { get; init; } = [];
    [JsonRequired] public int? ImperialSealId { get; init; }
    [JsonRequired] public San9.Core.Domain.ImperialCourtDefinition ImperialCourt { get; init; } = San9.Core.Domain.ImperialCourtDefinition.Empty;
    [JsonRequired] public bool ImperialCourtEnded { get; init; }
    [JsonRequired] public OfficerInitialState[] Officers { get; init; } = [];
    [JsonRequired] public San9.Core.Domain.UnitState[] Units { get; init; } = [];
    [JsonRequired] public InformationDefinitions Information { get; init; } = InformationDefinitions.Empty;
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(ScenarioId) || Year < 1 || Month is < 1 or > 12 || Day is < 1 or > 30 ||
            SearchEvents.IsDefault || Forces.Select(f => f.Id).Distinct().Count() != Forces.Length ||
            !Forces.Any(f => f.Id == PlayerForceId && (!f.IsDormant || SearchEvents.OfType<San9.Core.Domain.ForceActivationSearchEventDefinition>().Any(d => d.ForceId == f.Id))))
            throw new InvalidDataException("Invalid scenario date or player force.");
        Information.Validate(this);
        try { Alliance?.Validate(Forces.Where(f => !f.IsDormant && !f.IsBarbarian).Select(f => f.Id).ToHashSet()); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid initial alliance.", error); }
        if (Military is { } military)
        {
            var rules = new San9.Core.Domain.MilitaryRulebook(military);
            var personIds = Officers.Select(o => o.State.Id).ToHashSet();
            if (rules.LinkRules.Pairs.Any(p => !personIds.Contains(p.FirstId) || !personIds.Contains(p.SecondId)))
                throw new InvalidDataException("Unknown tactic relationship person reference.");
            if (rules.RegionIds.OfType<int>().Any(id => !RegionNameKeys.ContainsKey(id)))
                throw new InvalidDataException("Unknown battlefield region reference.");
            if (rules.ConstructionSites.Values.Any(site => !RegionNameKeys.ContainsKey(site.RegionId) || !RegionCityIds.ContainsKey(site.RegionId)))
                throw new InvalidDataException("Unknown construction region.");
            if (Officers.Any(o => o.State.EquippedTacticId is int id && !rules.Tactics.ContainsKey(id)))
                throw new InvalidDataException("Unknown equipped tactic reference.");
            if (!rules.Places.Keys.Order().SequenceEqual(Settlements.Select(s => s.Id).Order()) ||
                Settlements.Any(s => rules.Places[s.Id].Position != s.Position))
                throw new InvalidDataException("Military place identities or positions differ from settlements.");
        }
        if (CityConnections.IsDefault || UnbuiltPlaces.IsDefault || UnbuiltPlaces.Any(p => p is null || !RegionNameKeys.ContainsKey(p.Id) || Settlements.Any(s => s.Id == p.Id)))
            throw new InvalidDataException("Invalid unbuilt personnel place.");
        try
        {
            if (SearchEvents.IsDefault || SearchEvents.Any(e => e is null)) throw new InvalidDataException("Invalid search event collection.");
            _ = new San9.Core.Domain.PersonnelRulebook(Settlements.Select(s => new San9.Core.Domain.PersonPlace(s.Id, s.Position)).Concat(UnbuiltPlaces), RegionCityIds, CityConnections, CityProvinceIds, searchEvents: SearchEvents);
            var ids = Officers.Select(o => o.State.Id).ToHashSet();
            foreach (var offer in SearchEvents.OfType<San9.Core.Domain.MysticBookOfferSearchEventDefinition>())
                if (!ids.Contains(offer.MysticOfficerId) || !Information.Treasures.Any(t => t.Id == offer.TreasureId))
                    throw new InvalidDataException("Unknown mystic book offer reference.");
            foreach (var insult in SearchEvents.OfType<San9.Core.Domain.ScholarInsultSearchEventDefinition>())
                if (!ids.Contains(insult.ScholarOfficerId)) throw new InvalidDataException("Unknown scholar insult reference.");
            foreach (var longevity in SearchEvents.OfType<San9.Core.Domain.LongevitySearchEventDefinition>())
                if (!ids.Contains(longevity.DivinerOfficerId)) throw new InvalidDataException("Unknown longevity reference.");
            foreach (var diviner in SearchEvents.OfType<San9.Core.Domain.DivinerVisitSearchEventDefinition>())
                if (!ids.Contains(diviner.DivinerOfficerId)) throw new InvalidDataException("Unknown diviner visit reference.");
            foreach (var physician in SearchEvents.OfType<San9.Core.Domain.PhysicianVisitSearchEventDefinition>())
                if (physician.Sites.Any(s => !ids.Contains(s.PhysicianOfficerId) || !Information.Treasures.Any(t => t.Id == s.TreasureId)))
                    throw new InvalidDataException("Unknown physician visit reference.");
            foreach (var sage in SearchEvents.OfType<San9.Core.Domain.SageVisitSearchEventDefinition>())
                if (!ids.Contains(sage.SageOfficerId) || sage.RecommendedOfficerIds.Any(id => !ids.Contains(id)))
                    throw new InvalidDataException("Unknown sage visit officer reference.");
            ChainedPlot?.Validate(ids);
            ThreeVisits?.Validate(ids, Settlements.Select(s => s.Id).Concat(UnbuiltPlaces.Select(p => p.Id)).ToHashSet());
            ThreeVisits?.ValidateStarted(ThreeVisitsStarted, new(Year, Month, Day), ConsumedEventKeys);
            if (ThreeVisits is null && ThreeVisitsStarted is not null) throw new InvalidDataException("Three Visits state requires a definition.");
            foreach (var healing in SearchEvents.OfType<San9.Core.Domain.HealingSearchEventDefinition>())
                if (!ids.Contains(healing.HealerOfficerId) || healing.ExcludedSearchingOfficerIds.Any(id => !ids.Contains(id)))
                    throw new InvalidDataException("Unknown search event officer reference.");
            SunMarriage?.Validate(ids, Military?.Tactics.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? []);
            var tactics = Military?.Tactics.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? [];
            foreach (var bookReturn in SearchEvents.OfType<San9.Core.Domain.MysticBookReturnSearchEventDefinition>())
                if (!ids.Contains(bookReturn.MysticOfficerId) || !Information.Treasures.Any(t => t.Id == bookReturn.TreasureId) ||
                    bookReturn.RewardTacticIds.Any(id => !tactics.Contains(id))) throw new InvalidDataException("Unknown mystic book return reference.");
            foreach (var visit in SearchEvents.OfType<San9.Core.Domain.WomenVisitSearchEventDefinition>())
                if (visit.Sites.Any(s => !ids.Contains(s.VisitorOfficerId) || s.RequiredLeaderIds.Any(id => !ids.Contains(id)) ||
                    s.RewardTacticIds.Any(id => !tactics.Contains(id))))
                    throw new InvalidDataException("Unknown women visit reference.");
            var historicalCities = Settlements.Where(s => s.Kind == San9.Core.Domain.SettlementKind.City).Select(s => s.Id).ToHashSet();
            foreach (var activation in SearchEvents.OfType<San9.Core.Domain.ForceActivationSearchEventDefinition>())
            {
                activation.ValidateReferences(Forces.Select(f => f.Id).ToHashSet(), historicalCities,
                    Settlements.Select(s => s.Id).ToHashSet(), ids, Military?.RegionIds.IsEmpty == false);
                var city = Settlements.Single(s => s.Id == activation.CityId);
                var force = Forces.Single(f => f.Id == activation.ForceId);
                if (city.SoldierCapacity < activation.CitySoldiers)
                    throw new InvalidDataException("Activation troops exceed the city's capacity.");
                if (force.IsDormant && (!city.IsDormant || city.OwnerId != force.Id ||
                    force.LeaderId is not int leader || !activation.OfficerIds.Contains(leader) ||
                    force.AdviserId is int adviser && !activation.OfficerIds.Contains(adviser) ||
                    activation.AttachedSettlementIds.Any(id => !Settlements.Single(s => s.Id == id).IsDormant) ||
                    activation.OfficerIds.Any(id => !Officers.Any(p => p.State.Id == id && p.State.IsDormant &&
                        p.State.OwnerId == force.Id && p.State.SettlementId == city.Id))))
                    throw new InvalidDataException("Invalid dormant force activation group.");
            }
            ShanyangGong?.Validate(ids, Settlements.Select(s => s.Id).Concat(UnbuiltPlaces.Select(p => p.Id)).ToHashSet(), Information.Ranks.Length > 0);
            RedCliffs?.Validate(ids, historicalCities, Settlements.Select(s => s.Id).Concat(UnbuiltPlaces.Select(p => p.Id)).ToHashSet(), Military?.RegionIds.IsEmpty == false);
            Guandu?.Validate(ids, Settlements.Select(s => s.Id).Concat(UnbuiltPlaces.Select(p => p.Id)).ToHashSet(), Military?.RegionIds.IsEmpty == false);
            HanzhongKing?.Validate(ids, historicalCities, Information.Ranks.Select(r => r.Id).ToHashSet());
            FiveTigers?.Validate(ids, tactics);
            ExpeditionMemorial?.Validate(ids, CityProvinceIds, historicalCities, Information.CountryTitles.Select(t => t.Id).ToHashSet(), tactics);
            foreach (var monument in SearchEvents.OfType<San9.Core.Domain.TreasureMonumentSearchEventDefinition>())
                if (!Settlements.Any(s => s.Id == monument.PlaceId) || !Information.Ranks.Any(r => r.Id == monument.MinimumRankId) ||
                    !Information.Treasures.Any(t => t.Id == monument.TreasureId))
                    throw new InvalidDataException("Unknown treasure monument reference.");
            foreach (var ceremony in SearchEvents.OfType<San9.Core.Domain.ImperialCeremonySearchEventDefinition>())
                if (!Information.Ranks.Any(r => r.Id == ceremony.RequiredRankId)) throw new InvalidDataException("Unknown ceremony rank reference.");
            foreach (var scholar in SearchEvents.OfType<San9.Core.Domain.ScholarReturnSearchEventDefinition>())
                if (!ids.Contains(scholar.ScholarOfficerId) || scholar.RewardTacticIds.Any(id => !tactics.Contains(id)))
                    throw new InvalidDataException("Unknown scholar return reference.");
            foreach (var beast in SearchEvents.OfType<San9.Core.Domain.BeastSearchEventDefinition>())
                if (beast.BattleTacticIds.Concat(beast.Sites.SelectMany(s => s.RewardTacticIds)).Any(id => !tactics.Contains(id)))
                    throw new InvalidDataException("Unknown or unlearnable search event tactic reference.");
        }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid personnel geography.", error); }
        if (FoodExchangeRate <= 0) throw new InvalidDataException("Invalid food exchange rate.");
        try { FoodMarket?.Validate(); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid food market.", error); }
        new San9.Core.Domain.EconomyRulebook([], DomesticPairs, ImperialSealId).Validate();
        try { QiaoMarriage?.Validate(Officers.Select(o => o.State.Id).ToHashSet(), CityProvinceIds); }
        catch (ArgumentException e) { throw new InvalidDataException("Invalid historical event definition.", e); }
        if (ImperialCourt is null) throw new InvalidDataException("Missing imperial court definition.");
        try { ImperialCourt.Validate(Officers.Select(o => o.State.Id).ToHashSet()); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid imperial court references.", error); }
        if (DomesticPairs.Any(p => !Officers.Any(o => o.State.Id == p.FirstId) || !Officers.Any(o => o.State.Id == p.SecondId)) ||
            ImperialSealId is int seal && !Information.Treasures.Any(t => t.Id == seal)) throw new InvalidDataException("Invalid economy references.");
        if (Officers.Any(o => o.State.RegionId is int region && !UnbuiltPlaces.Any(p => p.Id == region))) throw new InvalidDataException("Invalid officer region.");
        if (ConsumedEventKeys.IsDefault || ConsumedEventKeys.Any(k => string.IsNullOrWhiteSpace(k.Value)) ||
            ConsumedEventKeys.Distinct().Count() != ConsumedEventKeys.Length)
            throw new InvalidDataException("Invalid initial consumed event keys.");
        var forceIds = Forces.Select(f => f.Id).ToHashSet();
        if (AttackRequests.IsDefault || AttackRequests.Any(r => r is null || r.RemainingXun <= 0 ||
            r.RequestedForceId == r.RequesterForceId || !forceIds.Contains(r.RequestedForceId) || !forceIds.Contains(r.RequesterForceId) ||
            !Settlements.Any(s => s.Id == r.TargetSettlementId && s.OwnerId is int owner && forceIds.Contains(owner) &&
                owner != r.RequestedForceId && owner != r.RequesterForceId)) ||
            AttackRequests.Select(r => r.RequestedForceId).Distinct().Count() != AttackRequests.Length)
            throw new InvalidDataException("Invalid initial attack requests.");
        if (Units.Select(u => u.Id).Distinct().Count() != Units.Length || Units.Any(u => !u.IsValid || !forceIds.Contains(u.OwnerId) ||
            u.ServicePlaceId is int home && !Settlements.Any(s => s.Id == home) && !UnbuiltPlaces.Any(p => p.Id == home)))
            throw new InvalidDataException("Invalid field unit initial state.");
        if (Forces.Any(f => f.EventHistory.IsDefault || f.EventHistory.Any(k => string.IsNullOrWhiteSpace(k.Value)) ||
            f.EventHistory.Distinct().Count() != f.EventHistory.Length)) throw new InvalidDataException("Invalid force event history.");
        if (Forces.Any(f => f.ProvidedTactics.IsDefault || f.ProvidedTactics.Any(t => t < 0) || f.ProvidedTactics.Distinct().Count() != f.ProvidedTactics.Length || f.Id < 0 || string.IsNullOrWhiteSpace(f.NameKey) || f.Trust is < 0 or > 1000 || f.BaseTrust is < 0 or > 1000 || f.ColorCode is < 0 or > 50 || f.FlagGlyph is < -1 or > 185))
            throw new InvalidDataException("Invalid force snapshot.");
        if (Settlements.Select(s => s.Id).Distinct().Count() != Settlements.Length || Settlements.Any(s =>
            s.Id < 0 || !Enum.IsDefined(s.Kind) || string.IsNullOrWhiteSpace(s.NameKey) || s.OwnerId is int owner && !forceIds.Contains(owner) ||
            s.X < 0 || s.Y < 0 || s.Morale is < 0 or > 100 ||
            s.MerchantPresenceChance is < 0 or > 100 || s.Kind != San9.Core.Domain.SettlementKind.City && s.MerchantPresenceChance != 0 ||
            s.Icons.Distinct().Count() != s.Icons.Length || s.Icons.Any(i => i is not ("training" or "emperor")) ||
            s.Kind != San9.Core.Domain.SettlementKind.City && s.Icons.Length != 0 ||
            s.Appearance is < 0 or > 2 || s.PublicOrder is < 0 or > 1000 || s.Commerce < 0 || s.Commerce > s.MaxCommerce || s.Cultivation < 0 || s.Cultivation > s.MaxCultivation ||
            s.SoldierCapacity is 0 or > 500000 || s.Soldiers > s.SoldierCapacity || s.Wounded > San9.Core.Domain.MilitaryRulebook.WoundedCapacity || s.Population > 1000000 || s.MaxCommerce > 2500 || s.MaxCultivation > 4000 || s.Soldiers == 0 && s.Morale != 0 || s.MaxDurability <= 0 || s.Durability < 0 || s.Durability > s.MaxDurability))
            throw new InvalidDataException("Invalid settlement snapshot.");
        var settlementIds=Settlements.Select(s=>s.Id).ToHashSet();
        if (Settlements.Any(s => !s.IsDormant && Forces.Any(f => f.Id == s.OwnerId && f.IsDormant)) ||
            Officers.Any(o => o.State.IsDormant && o.State.UnitId is not null || !o.State.IsDormant &&
                (Forces.Any(f => f.Id == o.State.OwnerId && f.IsDormant) || Settlements.Any(s => s.Id == o.State.SettlementId && s.IsDormant))) ||
            Forces.Any(f => !f.IsDormant && Officers.Any(o => o.State.IsDormant && (o.State.Id == f.LeaderId || o.State.Id == f.AdviserId))) ||
            Units.Any(u => Forces.Any(f => f.Id == u.OwnerId && f.IsDormant)))
            throw new InvalidDataException("Dormant initial objects cannot participate in the active world.");
        if (Officers.Any(o => string.IsNullOrWhiteSpace(o.NameKey) || o.State is null || !o.State.IsValid()) ||
            Officers.Select(o => o.State.Id).Distinct().Count() != Officers.Length || Officers.Any(o =>
                o.State.ForceLoyalties.Keys.Any(id => !forceIds.Contains(id)) ||
                o.State.SettlementId is int location && !settlementIds.Contains(location) ||
                o.State.OwnerId is int owner && !forceIds.Contains(owner) ||
                o.State.UnitId is int unit && !Units.Any(u => u.Id == unit &&
                    (o.State.Status == San9.Core.Domain.OfficerStatus.Captive ? u.OwnerId != o.State.OwnerId : u.OwnerId == o.State.OwnerId)) ||
                o.State.IsServing && o.State.SettlementId is int home && Settlements.Single(s => s.Id == home).OwnerId != o.State.OwnerId))
            throw new InvalidDataException("Invalid officer initial state.");
        if (Forces.Any(f => f.LeaderId is int leader && !Officers.Any(o => o.State.Id == leader && o.State.OwnerId == f.Id)))
            throw new InvalidDataException("Invalid force leader reference.");
        if (Forces.Any(f => f.AdviserId is int adviser && !Officers.Any(o => o.State.Id == adviser && o.State.OwnerId == f.Id && o.State.Status == San9.Core.Domain.OfficerStatus.Strategist)) ||
            Officers.Any(o => o.State.Status == San9.Core.Domain.OfficerStatus.Strategist && !Forces.Any(f => f.Id == o.State.OwnerId && f.AdviserId == o.State.Id)))
            throw new InvalidDataException("Invalid force adviser reference.");
        if (Settlements.Any(s => s.CorpsId < 0 || s.CorpsId is not null && s.OwnerId is null) ||
            Settlements.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId).Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new InvalidDataException("Invalid direct corps ownership.");
        if (TacticCourses.IsDefault || TacticCourses.Any(c => c is null ||
            !Settlements.Any(s => s.Id == c.CityId && s.Kind == San9.Core.Domain.SettlementKind.City) ||
            Military?.Tactics.Any(t => t.Id == c.TacticId && t.Category is >= 0 and <= 8) != true) ||
            TacticCourses.Select(c => c.CityId).Distinct().Count() != TacticCourses.Length)
            throw new InvalidDataException("Invalid city tactic courses.");
        var cityIds = Settlements.Where(s => s.Kind == San9.Core.Domain.SettlementKind.City).Select(s => s.Id).ToHashSet();
        try
        {
            Terminal?.Validate(cityIds);
            Ending?.Validate(cityIds, Information.Treasures.Select(t => t.Id).ToHashSet());
            if (Ending is not null && Terminal is null) throw new ArgumentException("Ending content requires campaign victory rules.");
        }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid terminal or ending content.", error); }
        if (CityProvinceIds is null || CityProvinceIds.Any(p => !cityIds.Contains(p.Key) || p.Value < 0))
            throw new InvalidDataException("Invalid city province membership.");
        if (RegionCityIds.Any(p => !RegionNameKeys.ContainsKey(p.Key) || !cityIds.Contains(p.Value)))
            throw new InvalidDataException("Invalid region city reference.");
        if (FacilityNameSuffixKeys is null || FacilityNameSuffixKeys.Any(p => string.IsNullOrWhiteSpace(p.Value) || Military is null || !Military.Facilities.Any(f => f.Id == p.Key)))
            throw new InvalidDataException("Invalid facility name suffix references.");
        if (RegionNameKeys.Any(p => p.Key < 0 || string.IsNullOrWhiteSpace(p.Value))) throw new InvalidDataException("Invalid region names.");
    }
}
