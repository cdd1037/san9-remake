using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>One scenario-owned compilation of authored definitions, shared by simulation and projections.</summary>
internal sealed class ScenarioRules
{
    private readonly ScenarioInitialState _definition;
    internal EconomyRulebook Economy { get; }
    internal PersonnelRulebook Personnel { get; }
    internal TreasureRulebook Treasures { get; }
    internal GovernanceRulebook Governance { get; }
    internal MilitaryRulebook? Military { get; }
    internal PoliticalRulebook Politics { get; }
    internal OfficerAbilityRules Abilities { get; }

    internal ScenarioRules(ScenarioInitialState definition)
    {
        _definition = definition;
        Economy = new EconomyRulebook(definition.Information.Offices.Select(o => new OfficeSalary(o.Id, o.Salary)).ToImmutableArray(),
            definition.DomesticPairs, definition.ImperialSealId)
        { FoodMarket = definition.FoodMarket, ImperialCourt = definition.ImperialCourt, RegionCityIds = definition.RegionCityIds.ToImmutableDictionary() };
        Personnel = new PersonnelRulebook(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position)).Concat(definition.UnbuiltPlaces), definition.RegionCityIds, definition.CityConnections, definition.CityProvinceIds, definition.TacticCourses, definition.SearchEvents);
        Treasures = new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule()));
        Governance = new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            definition.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
        Military = definition.Military is { } militaryDefinition ? new MilitaryRulebook(militaryDefinition) : null;
        Politics = new PoliticalRulebook(definition.PoliticalEvents,
            definition.Information.Ranks.Select(r => new PeerageRule(r.Id, r.Precedence, r.RequiredTrust, r.LoyaltyBonus)),
            definition.Information.CountryTitles.Select(t => t.Rule), definition.ProvinceCountryTitleIds);
        Abilities = new(Treasures, Governance);
    }

    internal SimulationEngine CreateEngine(WorldState world) => new(world, Economy, Personnel, Treasures, Governance,
        Military, _definition.Terminal, Politics, _definition.QiaoMarriage, _definition.SunMarriage,
        _definition.HanzhongKing, _definition.FiveTigers, _definition.ExpeditionMemorial, _definition.ThreeVisits,
        _definition.ChainedPlot, _definition.RedCliffs, _definition.ShanyangGong, _definition.Guandu);
}
