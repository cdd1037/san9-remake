using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Read projection of scenario.json for force selection; no independently authored or cached data.</summary>
public sealed record ScenarioSelectionState(int SchemaVersion, int Year, int Month, int Day,
    ForceInitialState[] Forces, SettlementInitialState[] Settlements, OfficerInitialState[] Officers,
    UnitState[] Units, InformationDefinitions Information, AllianceState? Alliance)
{
    public static ScenarioSelectionState From(ScenarioInitialState scenario) => new(scenario.SchemaVersion,
        scenario.Year, scenario.Month, scenario.Day, scenario.Forces, scenario.Settlements, scenario.Officers,
        scenario.Units, scenario.Information, scenario.Alliance);
}
