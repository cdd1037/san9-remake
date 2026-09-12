using San9.Core.Domain;

namespace San9.Core.Simulation;

internal sealed record RationShortage(ResourceAccount Account, long Demand, long Missing);
public sealed record SupplyMoraleLostEvent(GameClock Clock, ResourceAccount Account, int? UnitId, int? SettlementId,
    int PreviousMorale, int Morale) : DomainEvent;

internal static class MilitarySupply
{
    internal static int MoraleLoss(long demand, long missing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(demand);
        ArgumentOutOfRangeException.ThrowIfNegative(missing);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(missing, demand);
        return (int)(((System.Numerics.BigInteger)missing * 10 + demand - 1) / demand);
    }

    internal static void Apply(WorldMutation world, IReadOnlyList<RationShortage> shortages, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is null || shortages.Count == 0) return;
        var losses = shortages.ToDictionary(s => s.Account, s => MoraleLoss(s.Demand, s.Missing));
        // Resolve every account before any unit termination can reassign a corps or treasury.
        var units = world.Units.Values.OrderBy(u => u.Id).Select(u => (Unit: u, Account: ResourceAccounts.Resolve(world, u.OwnerId, u.CorpsId))).ToArray();
        foreach (var home in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood && s.Soldiers > 0).OrderBy(s => s.Id).ToArray())
        {
            var account = ResourceAccounts.Resolve(world, home.OwnerId!.Value, home.CorpsId);
            if (!losses.TryGetValue(account, out int loss) || home.Morale == 0) continue;
            int morale = Math.Max(0, home.Morale - loss);
            world.Replace(home with { Morale = morale });
            events.Add(new SupplyMoraleLostEvent(world.Clock, account, null, home.Id, home.Morale, morale));
        }
        HashSet<int> affected = [];
        foreach (var (unit, account) in units)
        {
            if (!losses.TryGetValue(account, out int loss)) continue;
            int morale = Math.Max(0, unit.Morale - loss);
            world.Replace(unit with { Morale = morale });
            events.Add(new SupplyMoraleLostEvent(world.Clock, account, unit.Id, null, unit.Morale, morale));
            if (morale == 0) affected.Add(unit.OwnerId);
        }
        foreach (var (unit, account) in units)
            if (losses.ContainsKey(account) && world.Units.TryGetValue(unit.Id, out var current) && current.Morale == 0)
                BattleAftermath.TerminateUnit(world, unit.Id, null, rules, events);
        if (affected.Count == 0) return;
        BattleAftermath.Reconcile(world, affected, rules, events);
        ResolutionPipeline.CheckConquest(world, events);
    }
}
