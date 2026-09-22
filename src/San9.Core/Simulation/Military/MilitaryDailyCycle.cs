using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

// Positive daily demand, with shortage determined against the balance before debit.
internal sealed record DailyRationAccount(ResourceAccount Account, bool Shortage);
public sealed record DailyAttritionEvent(GameClock Clock, ResourceAccount Account, int? UnitId, int? SettlementId,
    bool Starved, bool Routed, uint SoldierLoss, uint AddedWounded, uint Dead, int PreviousMorale, int Morale) : DomainEvent
{
    [JsonRequired] public Guid? TaskId { get; init; }
}

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
        var transports = world.Simulation.Tasks.Where(t => t.Reinforcements is not null)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id)
            .Select(t => (Task: t, Account: ResourceAccounts.Resolve(world, t.ForceId, ReinforcementTransport.Corps(world, t)))).ToArray();
        foreach (var (task, account) in transports)
        {
            var troops = task.Reinforcements!;
            if (batches.TryGetValue(account, out var batch) && batch.Shortage && troops.Soldiers > 0)
            {
                uint damage = SoldierLoss(troops.Soldiers);
                var loss = CombatMath.Loss(troops.Soldiers, damage, 50, troops.Wounded, MilitaryRulebook.WoundedCapacity);
                int morale = Math.Max(0, troops.Morale - 3);
                events.Add(new DailyAttritionEvent(world.Clock, account, null, null, true, false,
                    damage, loss.AddedWounded, loss.Dead, troops.Morale, morale) { TaskId = task.Id });
                troops = troops with { Soldiers = loss.RemainingSoldiers, Wounded = troops.Wounded + loss.AddedWounded, Morale = morale };
            }
            var updated = task with { Reinforcements = troops.Soldiers + troops.Wounded == 0 ? null : troops };
            if (task.Kind == UnitStrategies.Kind && (troops.Soldiers == 0 || troops.Morale == 0))
            {
                world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
                events.Add(new TaskCancelledEvent(task.Id, world.Clock, "reinforcement-unfit"));
                PersonnelReturns.Start(world, updated, rules.Personnel, events, afterDailyCommands: false);
            }
            else
                world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Replace(task, updated) });
        }
        foreach (var home in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood && s.Soldiers > 0).OrderBy(s => s.Id).ToArray())
        {
            var account = ResourceAccounts.Resolve(world, home.OwnerId!.Value, home.CorpsId);
            if (!batches.TryGetValue(account, out var batch) || !batch.Shortage) continue;
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
            if (!batches.TryGetValue(account, out var batch) || !batch.Shortage && !routed) continue;
            uint damage = SoldierLoss(unit.Soldiers);
            var loss = CombatMath.Loss(unit.Soldiers, damage, military.CombatFormation(unit)?.WoundedPercent ?? 50,
                unit.Wounded, MilitaryRulebook.WoundedCapacity);
            int morale = Math.Max(0, unit.Morale - 3);
            world.Replace(unit with { Soldiers = loss.RemainingSoldiers, Wounded = unit.Wounded + loss.AddedWounded, Morale = morale });
            events.Add(new DailyAttritionEvent(world.Clock, account, unit.Id, null, batch.Shortage, routed,
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
