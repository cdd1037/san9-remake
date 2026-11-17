using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal sealed class ScenarioSessionDefinition
{
    private readonly HashSet<int> _playerForces;
    internal ScenarioSessionDefinition(ScenarioInitialState definition, IReadOnlyCollection<int>? playerForceIds = null,
        SessionDefinitions? definitions = null)
    {
        Definitions = definitions ?? SessionDefinitions.Empty;
        Definitions.Validate();
        definition.Validate();
        _playerForces = (playerForceIds ?? (definition.PlayerForceId is int player ? [player] : [])).ToHashSet();
        if (_playerForces.Any(id => !definition.Forces.Any(f => f.Id == id) && !Definitions.Forces.Any(f => f.Id == id)) ||
            playerForceIds is not null && _playerForces.Count != playerForceIds.Count)
            throw new ArgumentException("Invalid player force selection.");
        InitialPlayerForceIds = _playerForces.Order().ToImmutableArray();
        definition = ActivatePlayerForces(definition, InitialPlayerForceIds);
        if (_playerForces.Any(id => definition.Forces.Any(f => f.Id == id && f.IsDormant)))
            throw new ArgumentException("Selected force cannot be activated.");
        definition = definition with { PlayerForceId = InitialPlayerForceIds.IsEmpty ? null : InitialPlayerForceIds[0] };
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        Content = definition with
        {
            Information = definition.Information.Copy(), Forces = definition.Forces.ToArray(),
            Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            FacilityNameSuffixKeys = new(definition.FacilityNameSuffixKeys), RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds),
            ProvinceNameKeys = new(definition.ProvinceNameKeys), CityProvinceIds = new(definition.CityProvinceIds)
        };
        Rules = new(Content);
    }

    internal ScenarioInitialState Content { get; }
    internal SessionDefinitions Definitions { get; }
    internal ImmutableArray<int> InitialPlayerForceIds { get; }

    internal static ScenarioInitialState ActivatePlayerForces(ScenarioInitialState definition, IReadOnlyCollection<int> playerForceIds)
    {
        foreach (int id in playerForceIds) definition = ActivatePlayerForce(definition, id);
        return definition;
    }

    private static ScenarioInitialState ActivatePlayerForce(ScenarioInitialState definition, int forceId)
    {
        var activation = definition.SearchEvents.OfType<ForceActivationSearchEventDefinition>()
            .SingleOrDefault(d => d.ForceId == forceId);
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
    internal ScenarioRules Rules { get; }
    internal MilitaryRulebook? MilitaryRules => Rules.Military;
    internal StrategySnapshot CreatePreview()
    {
        var clock = new GameClock(Content.Year, Content.Month, Content.Day);
        var world = new WorldSnapshot(clock, new(clock, 0))
        {
            DefaultLoyalty = Content.DefaultLoyalty,
            Forces = Forces().ToArray(), Settlements = Settlements().ToArray(),
            Officers = Content.Officers.Select(o => o.CreateState(Content.Year)).ToArray(), Units = Content.Units,
            Corps = Content.Information.Corps, Relations = Content.Information.Relations, Treasures = Content.Information.TreasureStates
        };
        return new StrategySnapshotSource(Content, Rules, Definitions).Project(world);
    }
    internal WorldState CreateWorld(ulong seed, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false,
        ControllerKind playerForceController = ControllerKind.Player, NewGameSettings? settings = null) => WorldState.Create(new(Content.Year, Content.Month, Content.Day), seed,
        Forces(),
        BarbarianStartup.CreateSettlements(Forces(), Settlements(), Content.Officers.Select(o => o.State)),
        Content.Officers.Select(o => o.CreateState(Content.Year)), Content.Units, Content.Information.Corps,
        Content.Information.TreasureStates, Content.Information.Relations,
        Content.Forces.Where(f => !f.IsDormant).OrderBy(f => _playerForces.Contains(f.Id) ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(_playerForces.Contains(f.Id) && playerForceController == ControllerKind.Player ? "player" : $"ai:{f.Id}", f.Id,
                _playerForces.Contains(f.Id) ? playerForceController : ControllerKind.Automatic)), computerDamageBonusPercent, Content.Alliance,
        Content.AttackRequests.Select(r => new AttackRequestState(r.RequestedForceId, r.RequesterForceId, r.TargetSettlementId,
            Content.Settlements.Single(s => s.Id == r.TargetSettlementId).OwnerId!.Value, r.RemainingXun)), Content.AnnualAppearancesEnabled, Content.NaturalDeathsEnabled, Content.FreeOfficerMigrationEnabled, battleDeathsEnabled, Content.OfficeRequestsEnabled, Content.ConsumedEventKeys, Content.ForceConsolidationEnabled, Content.ForceEmergenceEnabled, Content.CityIndependenceEnabled, Content.ImperialCourtEnded, Content.ThreeVisitsStarted, Content.FoodExchangeRate, Content.SettlementDefectionsEnabled, Content.UnitDefectionsEnabled,
        settings ?? new NewGameSettings { Difficulty = Content.OriginalAi.Advanced ? GameDifficulty.Advanced : GameDifficulty.Beginner }, OfficerStartingPlaces(), _playerForces,
        Definitions.Forces.Select(f => f.Id).ToHashSet(), Definitions.Officers.Select(p => p.Id).ToHashSet(), Content.DefaultLoyalty);

    private int[] OfficerStartingPlaces()
    {
        var barbarianForces = Content.Forces.Where(f => f.IsBarbarian).Select(f => f.Id).ToHashSet();
        var cities = Content.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant &&
            (s.OwnerId is not int owner || !barbarianForces.Contains(owner))).Select(s => s.Id).ToHashSet();
        bool InEligibleCity(int id) => Content.RegionCityIds.TryGetValue(id, out int city) && cities.Contains(city);
        return Content.Settlements.Where(s => !s.IsDormant && s.CanHostPersons &&
                (s.Kind == SettlementKind.City ? cities.Contains(s.Id) : InEligibleCity(s.Id))).Select(s => s.Id)
            .Concat(Content.UnbuiltPlaces.Where(p => InEligibleCity(p.Id)).Select(p => p.Id))
            .Distinct().Order().ToArray();
    }

    private IEnumerable<ForceState> Forces() => Content.Forces.Select(f => new ForceState(f.Id, f.Trust)
    {
        ColorCode = f.ColorCode,
        FormationGrants = f.FormationGrants.OrderBy(g => g.FormationId).ToImmutableArray(),
        ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray(), IsBarbarian = !_playerForces.Contains(f.Id) && f.IsBarbarian, IsDormant = f.IsDormant,
        EventHistory = f.EventHistory.OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray(), HasDeposedEmperor = f.HasDeposedEmperor, LeaderId = f.LeaderId, AdviserId = f.AdviserId, RankId = f.RankId, CountryTitleId = f.CountryTitleId, BaseTrust = f.BaseTrust
    });

    private IEnumerable<SettlementState> Settlements() => Content.Settlements.Select(s => new SettlementState(s.Id,
        s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability, s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce,
        s.Cultivation, s.MaxCultivation)
    {
        EnabledTacticIds = MilitaryRules?.Places.GetValueOrDefault(s.Id)?.EnabledTacticIds ?? [],
        MerchantPresenceChance = s.MerchantPresenceChance, IsDormant = s.IsDormant, Kind = s.Kind, CorpsId = s.CorpsId, CanHostPersons = s.CanHostPersons, SoldierCapacity = s.SoldierCapacity, ConsumesFood = s.ConsumesFood, Population = s.Population, DraftPopulation = s.DraftPopulation, Wounded = s.Wounded
    });
}
