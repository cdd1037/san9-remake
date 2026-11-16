using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Current command and formation limits for deploying or adding active soldiers.</summary>
public static class UnitSoldierCapacity
{
    public static uint Read(WorldSnapshot world, UnitState unit, GovernanceRulebook governance, MilitaryRulebook military)
    {
        var force = world.Forces.Single(f => f.Id == unit.OwnerId);
        var commander = world.Officers.FirstOrDefault(p => p.Id == unit.CommanderId);
        return Math.Min(CommanderCapacity.Read(force, commander, governance), military.FormationCapacity(unit));
    }

    internal static uint Read(IWorldQuery world, UnitState unit, GovernanceRulebook governance, MilitaryRulebook military)
    {
        var commander = unit.CommanderId is int id ? world.Officers[id] : null;
        return Math.Min(CommanderCapacity.Read(world.Forces[unit.OwnerId], commander, governance), military.FormationCapacity(unit));
    }

    public static uint Remaining(uint soldiers, uint capacity) => capacity - Math.Min(soldiers, capacity);
}
