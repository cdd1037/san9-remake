using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Snapshot target eligibility shared with final order validation; never executes a command.</summary>
public sealed class MilitaryOrderQuery(WorldSnapshot snapshot, MilitaryRulebook rules)
{
    private readonly WorldMutation world = WorldState.Restore(snapshot).BeginMutation();

    public bool CanAssign(int unitId, MilitaryOrder order) => world.Units.TryGetValue(unitId, out var unit) &&
        !unit.IsEmbedded && !unit.IsPersonnel && unit.Soldiers > 0 && unit.FormationId is int formation &&
        rules.Formations.TryGetValue(formation, out var definition) && order.Kind != MilitaryOrderKind.Construct &&
        MilitaryMovement.ValidOrder(world, unit.OwnerId, order, definition, rules, unit.NavalFormationId);
}
