using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

/// <summary>Bounded ordinary-combat estimate, not a second simulation or a prediction of hidden tactics.</summary>
public static class CampaignCombatForecast
{
    public static CampaignForecast Evaluate(CampaignPlace target, ImmutableArray<CampaignForce> forces,
        ImmutableArray<CampaignWave> waves, int delay, Func<bool> spend)
    {
        if (forces.IsDefaultOrEmpty) return new(false, 0, 0, 0);
        int day = forces.Min(f => f.Action.Arrival!.Days!.Value) + delay;
        using var projection = Project(target, forces, waves, delay).GetEnumerator();
        while (spend())
        {
            if (!projection.MoveNext()) throw new InvalidOperationException("Combat projection ended without a result.");
            if (projection.Current is { } result) return result;
            day++;
        }
        return new(false, day, 0, forces.Sum(f => (long)f.Soldiers));
    }

    // Each yielded value represents one simulated combat day; null means the projection is still in progress.
    // The enumerator owns only local hypothesis state, never authoritative world objects or random streams.
    internal static IEnumerable<CampaignForecast?> Project(CampaignPlace target, ImmutableArray<CampaignForce> forces,
        ImmutableArray<CampaignWave> waves, int delay)
    {
        var soldiers = forces.Select(f => f.Soldiers).ToArray();
        uint defenders = target.Soldiers;
        long arrivedReinforcements = 0;
        int walls = target.Durability;
        long original = soldiers.Sum(s => (long)s);
        int arrival = forces.Max(f => f.Action.Arrival!.Days!.Value) + delay;
        // Ninety days bounds the forecast horizon. No assumed tactic proc can make an infeasible siege feasible.
        int firstDay = forces.Min(f => f.Action.Arrival!.Days!.Value) + delay;
        for (int day = firstDay; day <= arrival + 90; day++)
        {
            foreach (var wave in waves.Where(w => w.TargetId == target.Id && w.Days >= 0 && (w.Days == day || day == firstDay && w.Days < day)))
            {
                defenders = (uint)Math.Min(uint.MaxValue, (long)defenders + wave.Soldiers);
                arrivedReinforcements += wave.Soldiers;
            }
            bool retaliated = false;
            int active = forces.Where((f, i) => soldiers[i] > 0 && day >= f.Action.Arrival!.Days + delay).Count();
            for (int i = 0; i < forces.Length; i++)
            {
                var force = forces[i];
                if (day < force.Action.Arrival!.Days + delay || soldiers[i] == 0) continue;
                var attacker = new CombatStrength(soldiers[i], force.Morale, force.Leadership, 1, 10);
                var defender = new CombatStrength(defenders, target.Morale, target.Leadership, Math.Max(1, active), 10);
                uint loss = CombatMath.TroopDamage(attacker, defender, force.Profile, target.Profile, true, walls);
                uint reply = target.CanRetaliate && !retaliated ? CombatMath.TroopDamage(defender, attacker, target.Profile, force.Profile, false, 0) : 0;
                retaliated |= target.CanRetaliate;
                defenders -= loss;
                if (force.DamagesWalls) walls -= CombatMath.DurabilityDamage(soldiers[i], force.Leadership, force.Profile.AgainstWalls, walls);
                soldiers[i] -= reply;
                if ((defenders == 0 || walls == 0) && soldiers[i] > 0)
                {
                    long survivors = soldiers.Where((s, j) => day >= forces[j].Action.Arrival!.Days + delay).Sum(s => (long)s);
                    // Keep enough strength for the first counterattack window after capture, including a two-day arrival error.
                    // Reinforcements already fought are bounded by the remaining defenders. Wall collapse
                    // does not erase surviving field forces, but defeated forces cannot attack a second time.
                    long counter = Math.Min(arrivedReinforcements, defenders) +
                        waves.Where(w => w.TargetId == target.Id && w.Days > day && w.Days <= day + 12).Sum(w => (long)w.Soldiers);
                    yield return new(survivors >= Math.Max(1000, counter), day, survivors, original - soldiers.Sum(s => (long)s));
                    yield break;
                }
            }
            if (soldiers.All(s => s == 0) || day == arrival + 90) break;
            yield return null;
        }
        yield return new(false, arrival + 90, soldiers.Sum(s => (long)s), original - soldiers.Sum(s => (long)s));
    }
}
