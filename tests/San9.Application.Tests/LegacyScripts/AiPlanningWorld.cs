using San9.Application;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Candidate budgets apply to participating objects; authored dormant records cannot consume them.</summary>
internal static class AiPlanningWorld
{
    internal static WorldSnapshot Read(WorldSnapshot world)
    {
        var forces = world.Forces.Where(f => !f.IsDormant).ToArray();
        var owners = forces.Select(f => f.Id).ToHashSet();
        return world with
        {
            Forces = forces,
            Settlements = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed).ToArray(),
            Officers = world.Officers.Where(p => !p.IsDormant).ToArray(),
            Corps = world.Corps.Where(c => owners.Contains(c.OwnerId)).ToArray()
        };
    }
}
