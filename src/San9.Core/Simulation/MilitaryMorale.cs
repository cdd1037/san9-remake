using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record PeriodicMoraleLostEvent(GameClock Clock, int UnitId, int PreviousMorale, int Morale) : DomainEvent;

public sealed record GarrisonMoraleRecoveredEvent(GameClock Clock, int SettlementId, int PreviousMorale, int Morale) : DomainEvent;

internal static class MilitaryMorale
{
    internal static void Apply(WorldMutation world, IReadOnlyList<DailyRationAccount> accounts, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        var supplied = accounts.Where(s => !s.Exhausted).Select(s => s.Account).ToHashSet();
        if (world.Clock.Day % 3 == 0)
            foreach (var home in world.Settlements.Values.Where(s => s.OwnerId is not null && s.ConsumesFood && s.Soldiers > 0).OrderBy(s => s.Id).ToArray())
            {
                var force = world.Forces[home.OwnerId!.Value];
                if (force.IsBarbarian || !supplied.Contains(ResourceAccounts.Resolve(world, force.Id, home.CorpsId)) ||
                    home.Morale >= EconomyMath.RecruitMorale(force.Trust)) continue;
                world.Replace(home with { Morale = home.Morale + 1 });
                events.Add(new GarrisonMoraleRecoveredEvent(world.Clock, home.Id, home.Morale, home.Morale + 1));
            }
        HashSet<int> affected = [];
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            if (unit.IsEmbedded || unit.IsPersonnel || unit.FormationId is null || unit.Soldiers == 0 || unit.Morale == 0 ||
                unit.Condition == UnitCondition.Routed || world.Forces[unit.OwnerId].IsBarbarian ||
                !supplied.Contains(ResourceAccounts.Resolve(world, unit.OwnerId, unit.CorpsId)) ||
                military.HasCityInfluence(unit.OwnerId, unit.Position, world.Settlements.Values)) continue;
            int loss = world.Clock.Day % 2 == 1 ? 1 :
                world.Clock.Day % 4 == 0 && MilitaryEngagements.Engaged(world, unit, military) ? 1 : 0;
            if (loss == 0) continue;
            int morale = Math.Max(0, unit.Morale - loss);
            world.Replace(unit with { Morale = morale });
            events.Add(new PeriodicMoraleLostEvent(world.Clock, unit.Id, unit.Morale, morale));
            if (morale == 0) affected.Add(unit.OwnerId);
        }
        foreach (var unit in world.Units.Values.Where(u => u.Morale == 0 && affected.Contains(u.OwnerId)).OrderBy(u => u.Id).ToArray())
            BattleAftermath.TerminateUnit(world, unit.Id, null, rules, events);
        if (affected.Count == 0) return;
        BattleAftermath.Reconcile(world, affected, rules, events);
        ResolutionPipeline.CheckConquest(world, events);
    }
}
