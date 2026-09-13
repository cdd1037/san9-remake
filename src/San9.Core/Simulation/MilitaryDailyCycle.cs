using San9.Core.Domain;

namespace San9.Core.Simulation;

// Contains only accounts with positive demand, after their daily food debit.
internal sealed record DailyRationAccount(ResourceAccount Account, bool Exhausted);
public sealed record DailyAttritionEvent(GameClock Clock, ResourceAccount Account, int? UnitId, int? SettlementId,
    bool Starved, bool Routed, uint SoldierLoss, uint AddedWounded, uint Dead, int PreviousMorale, int Morale) : DomainEvent;

internal static class MilitaryDailyCycle
{
    internal static uint SoldierLoss(uint soldiers) => (uint)(((ulong)soldiers + 99) / 100);

    internal static void Apply(WorldMutation world, IReadOnlyList<DailyRationAccount> accounts, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military || accounts.Count == 0) return;
        var batches = accounts.ToDictionary(s => s.Account);
        // Resolve ownership before losses can terminate units and dissolve corps.
        var units = world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null)
            .OrderBy(u => u.Id).Select(u => (Unit: u, Account: ResourceAccounts.Resolve(world, u.OwnerId, u.CorpsId))).ToArray();
        foreach (var home in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood && s.Soldiers > 0).OrderBy(s => s.Id).ToArray())
        {
            var account = ResourceAccounts.Resolve(world, home.OwnerId!.Value, home.CorpsId);
            if (!batches.TryGetValue(account, out var batch) || !batch.Exhausted) continue;
            uint damage = SoldierLoss(home.Soldiers);
            var loss = CombatMath.Loss(home.Soldiers, damage, 50, home.Wounded, MilitaryRulebook.WoundedCapacity);
            int morale = Math.Max(0, home.Morale - 3);
            world.Replace(home with { Soldiers = loss.RemainingSoldiers, Wounded = home.Wounded + loss.AddedWounded, Morale = morale });
            events.Add(new DailyAttritionEvent(world.Clock, account, null, home.Id, true, false,
                damage, loss.AddedWounded, loss.Dead, home.Morale, morale));
        }
        HashSet<int> affected = [];
        List<int> defeated = [];
        foreach (var (unit, account) in units)
        {
            bool routed = unit.Condition == UnitCondition.Routed;
            if (!batches.TryGetValue(account, out var batch) || !batch.Exhausted && !routed) continue;
            uint damage = SoldierLoss(unit.Soldiers);
            var loss = CombatMath.Loss(unit.Soldiers, damage, military.CombatFormation(unit)?.WoundedPercent ?? 50,
                unit.Wounded, MilitaryRulebook.WoundedCapacity);
            int morale = Math.Max(0, unit.Morale - 3);
            world.Replace(unit with { Soldiers = loss.RemainingSoldiers, Wounded = unit.Wounded + loss.AddedWounded, Morale = morale });
            events.Add(new DailyAttritionEvent(world.Clock, account, unit.Id, null, batch.Exhausted, routed,
                damage, loss.AddedWounded, loss.Dead, unit.Morale, morale));
            if (morale == 0 || loss.RemainingSoldiers == 0)
            {
                affected.Add(unit.OwnerId);
                defeated.Add(unit.Id);
            }
        }
        // Ordinary morale must read the same account ownership before any defeat dissolves a corps.
        MilitaryMorale.Apply(world, accounts, rules, events);
        foreach (int id in defeated)
            if (world.Units.ContainsKey(id)) BattleAftermath.TerminateUnit(world, id, null, rules, events);
        if (affected.Count == 0) return;
        BattleAftermath.Reconcile(world, affected, rules, events);
        ResolutionPipeline.CheckConquest(world, events);
    }
}
