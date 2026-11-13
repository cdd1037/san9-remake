using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>One scenario-owned compilation of authored definitions, shared by simulation and projections.</summary>
internal sealed class ScenarioRules
{
    private readonly SimulationRules _simulation;
    internal EconomyRulebook Economy { get; }
    internal PersonnelRulebook Personnel { get; }
    internal TreasureRulebook Treasures { get; }
    internal EquipmentRulebook Equipment { get; }
    internal GovernanceRulebook Governance { get; }
    internal MilitaryRulebook? Military { get; }
    internal MilitaryCityNetwork MilitaryCities { get; }
    internal PoliticalRulebook Politics { get; }
    internal OfficerAbilityRules Abilities { get; }
    internal ImmutableDictionary<string, string> PersonalPeerageRegimeNames { get; }

    internal ScenarioRules(ScenarioInitialState definition)
    {
        var peerageRegimes = (definition.PersonalPeerageRegimes ?? []).ToImmutableDictionary(r => r.Id, r => new PersonalPeerageRegime(r.Periods), StringComparer.Ordinal);
        PersonalPeerageRegimeNames = (definition.PersonalPeerageRegimes ?? []).ToImmutableDictionary(r => r.Id, r => r.Name ?? r.Id, StringComparer.Ordinal);
        Economy = new EconomyRulebook(definition.Information.Offices.Select(o => new OfficeSalary(o.Id, o.Salary)).ToImmutableArray(),
            definition.DomesticPairs, definition.ImperialSealId)
        { FoodMarket = definition.FoodMarket, ImperialCourt = definition.ImperialCourt, RegionCityIds = definition.RegionCityIds.ToImmutableDictionary(), PersonalPeerageRegimes = peerageRegimes };
        Personnel = new PersonnelRulebook(definition.Settlements.Select(s => new PersonPlace(s.Id, s.Position)).Concat(definition.UnbuiltPlaces), definition.RegionCityIds, definition.CityConnections, definition.CityProvinceIds, definition.TacticCourses, definition.SearchEvents);
        var equipmentDefinitions = definition.Information.Equipment;
        bool builtInEquipment = equipmentDefinitions.Any(item => item.Id == "basic-spear" && item.ResearchProjectId == EquipmentPreset.ResearchId);
        bool authoredInitialIssue = equipmentDefinitions.Any(item => item.InitialIssue is not null);
        // Older upgraded packages can contain only converted treasures. Supply the same
        // recipes as an empty legacy catalog, while preserving explicitly authored catalogs.
        if (equipmentDefinitions.All(item => item.LegacyTreasureId is not null))
            equipmentDefinitions = equipmentDefinitions.AddRange(EquipmentPreset.Basic().Where(item => item.InitialIssue != true));
        else if (equipmentDefinitions.Any(item => item.Id == "basic-spear" && item.ResearchProjectId == EquipmentPreset.ResearchId) &&
                 equipmentDefinitions.All(item => item.Id != "basic-dart"))
            equipmentDefinitions = equipmentDefinitions.Add(EquipmentPreset.Basic().Single(item => item.Id == "basic-dart"));
        if (builtInEquipment)
            equipmentDefinitions = equipmentDefinitions.AddRange(EquipmentPreset.Basic().Where(p => (p.Grade > 1 || p.Id == "yellow-horse" || p.InitialIssue == true && !authoredInitialIssue) &&
                equipmentDefinitions.All(existing => existing.Id != p.Id)));
        // Only retrofit the unmodified built-in recipes; authored items keep their own purchase policy.
        var presets = EquipmentPreset.Basic().ToDictionary(item => item.Id, StringComparer.Ordinal);
        equipmentDefinitions = equipmentDefinitions.Select(item =>
        {
            if (item.PurchaseCost is not null || !presets.TryGetValue(item.Id, out var preset) ||
                item.LegacyTreasureId is not null || item.ResearchProjectId != preset.ResearchProjectId ||
                item.Slot != preset.Slot || item.Rarity != preset.Rarity || item.BaseBonuses != preset.BaseBonuses ||
                item.BonusPerLevel != preset.BonusPerLevel || item.ProductionCost != preset.ProductionCost ||
                item.ProductionDays != preset.ProductionDays || item.TacticId is not null || item.EscapeMount ||
                !item.CompatibleWeapons.IsEmpty || !(item.Name == preset.Name || item.Name == "制式" + preset.Name ||
                    item.Id == "basic-bow" && item.Name == "制式弓" || item.Id == "basic-shield" && item.Name == "制式盾" ||
                    item.Id == "basic-helmet" && item.Name == "制式头盔" || item.Id == "basic-armor" && item.Name == "制式铠甲")) return item;
            return item with { Name = preset.Name, PurchaseCost = preset.PurchaseCost };
        }).ToImmutableArray();
        Equipment = new EquipmentRulebook(LegacyEquipmentMigration.Convert(definition.Information.Treasures,
            definition.Information.TreasureStates, equipmentDefinitions).Definitions);
        Treasures = new TreasureRulebook(definition.Information.Treasures.Select(t => t.ToRule()), Equipment);
        Governance = new GovernanceRulebook(definition.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority)
            { AbilityBonuses = o.AbilityBonuses, Salary = o.Salary, RequiredRankId = o.RequiredRankId }),
            definition.Information.Ranks.ToDictionary(r => r.Id, r => r.Precedence));
        if (definition.Military is { } militaryDefinition)
        {
            militaryDefinition = MixedTroopPreset.UpgradeLegacyTroopLevels(militaryDefinition);
            if (Equipment.Items.Values.Any(e => e.ResearchProjectId == EquipmentPreset.ResearchId) &&
                !militaryDefinition.ResearchProjects.Any(p => p.Id == EquipmentPreset.ResearchId))
                militaryDefinition = militaryDefinition with { ResearchProjects = militaryDefinition.ResearchProjects.Add(EquipmentPreset.Research()) };
            Military = new MilitaryRulebook(militaryDefinition);
            if (Equipment.Items.Values.Any(e => e.ResearchProjectId is string id && !Military.ResearchProjects.ContainsKey(id) ||
                e.PurchaseResearchProjectId is string purchaseId && !Military.ResearchProjects.ContainsKey(purchaseId)))
                throw new InvalidDataException("Unknown equipment research project.");
        }
        MilitaryCities = new(definition.MilitaryCityConnections);
        Politics = new PoliticalRulebook(definition.PoliticalEvents,
            definition.Information.Ranks.Select(r => r.ToRule()),
            definition.Information.CountryTitles.Select(t => t.Rule), definition.ProvinceCountryTitleIds);
        Abilities = new(Treasures, Governance, definition.OfficerGrowth, Equipment);
        _simulation = new()
        {
            Economy = Economy, Personnel = Personnel, Treasures = Treasures, Equipment = Equipment, Governance = Governance, OfficerGrowth = definition.OfficerGrowth,
            Military = Military, Terminal = definition.Terminal, Politics = Politics,
            QiaoMarriage = definition.QiaoMarriage, SunMarriage = definition.SunMarriage,
            HanzhongKing = definition.HanzhongKing, FiveTigers = definition.FiveTigers,
            ExpeditionMemorial = definition.ExpeditionMemorial, ThreeVisits = definition.ThreeVisits,
            ChainedPlot = definition.ChainedPlot, RedCliffs = definition.RedCliffs,
            ShanyangGong = definition.ShanyangGong, Guandu = definition.Guandu
        };
    }

    internal SimulationEngine CreateEngine(WorldState world) => new(world, _simulation);
}
