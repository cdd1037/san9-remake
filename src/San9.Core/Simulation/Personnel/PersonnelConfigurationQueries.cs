using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    public ImmutableArray<GovernorOfficeRule> GetAvailableOffices(int forceId) => _rules.Governance.Offices.Values
        .Where(o => _rules.Governance.CanAppoint(Snapshot.Forces.Single(f => f.Id == forceId).RankId, o))
        .OrderBy(o => o.OfficeId).ToImmutableArray();

    public TreasureRule? GetTreasureDefinition(int treasureId) => _rules.Treasures.Items.GetValueOrDefault(treasureId);

    /// <summary>Pure attribute projection, also usable by authoring tools; does not predict recovery dice.</summary>
    public OfficerAbilities EvaluateOfficerAbilities(OfficerState officer, IEnumerable<TreasureState> holdings) =>
        _rules.Abilities.Evaluate(officer, holdings);
}
