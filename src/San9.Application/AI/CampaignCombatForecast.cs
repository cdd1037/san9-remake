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
        ImmutableArray<CampaignWave> waves, int delay, bool expectedTactics = false)
    {
        var soldiers = forces.Select(f => f.Soldiers).ToArray();
        uint defenders = target.Soldiers;
        long arrivedReinforcements = 0;
        int walls = target.Durability;
        long original = soldiers.Sum(s => (long)s);
        int arrival = forces.Max(f => f.Action.Arrival!.Days!.Value) + delay;
        // Ninety days bounds the forecast horizon. No assumed tactic proc can make an infeasible siege feasible.
        int firstDay = forces.Min(f => f.Action.Arrival!.Days!.Value) + delay;
        var arrivals = forces.Select(f => f.Action.Arrival!.Days!.Value + delay).ToArray();
        var reinforcements = waves.Where(w => w.TargetId == target.Id && w.Days >= 0).OrderBy(w => w.Days).ToArray();
        int nextReinforcement = 0;
        long remaining = original;
        for (int day = firstDay; day <= arrival + 90; day++)
        {
            while (nextReinforcement < reinforcements.Length && reinforcements[nextReinforcement].Days <= day)
            {
                var wave = reinforcements[nextReinforcement++];
                defenders = (uint)Math.Min(uint.MaxValue, (long)defenders + wave.Soldiers);
                arrivedReinforcements += wave.Soldiers;
            }
            bool retaliated = false;
            int active = 0;
            for (int i = 0; i < soldiers.Length; i++)
                if (soldiers[i] > 0 && day >= arrivals[i]) active++;
            for (int i = 0; i < forces.Length; i++)
            {
                var force = forces[i];
                if (day < arrivals[i] || soldiers[i] == 0) continue;
                var attacker = new CombatStrength(soldiers[i], force.Morale, force.Leadership, 1, 10);
                var defender = new CombatStrength(defenders, target.Morale, target.Leadership, Math.Max(1, active), 10);
                uint loss = CombatMath.TroopDamage(attacker, defender, force.Profile, target.Profile, true, walls);
                if (expectedTactics && force.Tactics is { } tactics)
                    loss = (uint)Math.Min(defenders, loss + (tactics.Damage + tactics.OrdinaryHits * loss) / CampaignTacticEstimate.Scale);
                uint reply = target.CanRetaliate && !retaliated ? CombatMath.TroopDamage(defender, attacker, target.Profile, force.Profile, false, 0) : 0;
                retaliated |= target.CanRetaliate;
                defenders -= loss;
                if (force.DamagesWalls) walls -= CombatMath.DurabilityDamage(soldiers[i], force.Leadership, force.Profile.AgainstWalls, walls);
                soldiers[i] -= reply;
                remaining -= reply;
                if ((defenders == 0 || walls == 0) && soldiers[i] > 0)
                {
                    long survivors = 0;
                    for (int j = 0; j < soldiers.Length; j++) if (day >= arrivals[j]) survivors += soldiers[j];
                    // Keep enough strength for the first counterattack window after capture, including a two-day arrival error.
                    // Reinforcements already fought are bounded by the remaining defenders. Wall collapse
                    // does not erase surviving field forces, but defeated forces cannot attack a second time.
                    long counter = Math.Min(arrivedReinforcements, defenders);
                    for (int j = nextReinforcement; j < reinforcements.Length && reinforcements[j].Days <= day + 12; j++) counter += reinforcements[j].Soldiers;
                    yield return new(survivors >= Math.Max(1000, counter), day, survivors, original - remaining);
                    yield break;
                }
            }
            if (remaining == 0 || day == arrival + 90) break;
            yield return null;
        }
        yield return new(false, arrival + 90, remaining, original - remaining);
    }
}
