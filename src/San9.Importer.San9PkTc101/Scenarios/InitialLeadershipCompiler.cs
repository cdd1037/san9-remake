using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>Source appointment bits are derived working state, not authored initial appointments.</summary>
public static class InitialLeadershipCompiler
{
    public static ScenarioInitialState Compile(ScenarioInitialState scenario)
    {
        var governance = new GovernanceRulebook(scenario.Information.Offices.Select(o =>
            new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority) { AbilityBonuses = o.AbilityBonuses }));
        var abilities = new OfficerAbilityRules(new(scenario.Information.Treasures.Select(t => t.ToRule())), governance)
            .EvaluateMany(scenario.Officers.Select(o => o.State), scenario.Information.TreasureStates);
        var places = scenario.Settlements.Where(s => !s.IsDormant && s.CanHostPersons).ToDictionary(s => s.Id);
        var commanders = scenario.Officers.Select(o => o.State)
            .Where(p => p.IsServing && p.SettlementId is int id && places.TryGetValue(id, out var place) && place.OwnerId == p.OwnerId)
            .GroupBy(p => p.SettlementId!.Value).Select(group => governance.SelectCommander(group, abilities)!.Id).ToHashSet();
        return scenario with { Officers = scenario.Officers.Select(o => o with
            { State = o.State with { IsCommander = commanders.Contains(o.State.Id) } }).ToArray() };
    }
}
