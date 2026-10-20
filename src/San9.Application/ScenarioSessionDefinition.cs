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
            ProvinceNameKeys = new(definition.ProvinceNameKeys), CityProvinceIds = new(definition.CityProvinceIds)
        };
        Rules = new(Content);
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
    internal ScenarioRules Rules { get; }
    internal MilitaryRulebook? MilitaryRules => Rules.Military;
    internal WorldState CreateWorld(ulong seed, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false,
        ControllerKind playerForceController = ControllerKind.Player) => WorldState.Create(new(Content.Year, Content.Month, Content.Day), seed,
        Forces(),
        BarbarianStartup.CreateSettlements(Forces(), Settlements(), Content.Officers.Select(o => o.State)),
        Content.Officers.Select(o => o.CreateState(Content.Year)), Content.Units, Content.Information.Corps,
        Content.Information.TreasureStates, Content.Information.Relations,
        Content.Forces.Where(f => !f.IsDormant).OrderBy(f => f.Id == Content.PlayerForceId ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(f.Id == Content.PlayerForceId && playerForceController == ControllerKind.Player ? "player" : $"ai:{f.Id}", f.Id,
                f.Id == Content.PlayerForceId ? playerForceController : ControllerKind.Automatic)), computerDamageBonusPercent, Content.Alliance,
        Content.AttackRequests.Select(r => new AttackRequestState(r.RequestedForceId, r.RequesterForceId, r.TargetSettlementId,
            Content.Settlements.Single(s => s.Id == r.TargetSettlementId).OwnerId!.Value, r.RemainingXun)), Content.AnnualAppearancesEnabled, Content.NaturalDeathsEnabled, Content.FreeOfficerMigrationEnabled, battleDeathsEnabled, Content.OfficeRequestsEnabled, Content.ConsumedEventKeys, Content.ForceConsolidationEnabled, Content.ForceEmergenceEnabled, Content.CityIndependenceEnabled, Content.ImperialCourtEnded, Content.ThreeVisitsStarted, Content.FoodExchangeRate, Content.SettlementDefectionsEnabled, Content.UnitDefectionsEnabled);

    private IEnumerable<ForceState> Forces() => Content.Forces.Select(f => new ForceState(f.Id, f.Trust)
    {
        ColorCode = f.ColorCode,
        AdditionalFormationIds = f.AdditionalFormationIds.Order().ToImmutableArray(),
        ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray(), IsBarbarian = f.Id != Content.PlayerForceId && f.IsBarbarian, IsDormant = f.IsDormant,
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
