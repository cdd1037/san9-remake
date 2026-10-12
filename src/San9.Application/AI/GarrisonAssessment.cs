using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Revalues frozen arrival waves against current garrisons without repeating navigation.</summary>
internal sealed class GarrisonAssessment
{
    private readonly MilitaryRulebook rules;
    private readonly ImmutableDictionary<int, OfficerAbilities> abilities;
    private readonly ILookup<int?, OfficerState> residents;
    private readonly HashSet<int> traveling;
    private readonly ILookup<int, ArrivalProjection> arrivals;
    private readonly Dictionary<(int Settlement, int? Departing), (uint Required, int? Deadline)> needs = [];

    internal GarrisonAssessment(WorldSnapshot world, int forceId, MilitaryRulebook rules,
        ImmutableDictionary<int, OfficerAbilities> abilities, IEnumerable<ArrivalProjection> committed)
    {
        this.rules = rules;
        this.abilities = abilities;
        residents = world.Officers.Where(p => p.OwnerId == forceId && p.IsServing).ToLookup(p => p.SettlementId);
        traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        arrivals = committed.Where(a => a.Hostile || a.UnitId is not null).ToLookup(a => a.TargetId);
    }

    private long Defense(SettlementState place, int? departing)
    {
        int leadership = residents[place.Id].Where(p => p.Id != departing && !traveling.Contains(p.Id))
            .Select(p => abilities[p.Id].Leadership).DefaultIfEmpty(50).Max();
        var strength = new CombatStrength(1000, place.Morale, leadership, 100, 100);
        return rules.Place(place).Combat is { } profile ? CombatMath.FormationStrength(strength, profile).Defense :
            CombatMath.PolicyStrength(strength);
    }

    internal (uint Required, int? Deadline) Need(SettlementState target, int? departing = null)
    {
        if (needs.TryGetValue((target.Id, departing), out var saved)) return saved;
        var targetArrivals = arrivals[target.Id];
        long power = 0;
        long Incoming(int? days) => Math.Min(Math.Max(0L, target.SoldierCapacity - (long)target.Soldiers),
            targetArrivals.Where(a => !a.Hostile && a.Estimate.Status == ArrivalEstimateStatus.Known &&
                (days is null || a.Estimate.Days <= days)).Sum(a => (long)a.Soldiers));
        uint required = (uint)Math.Max(0, 3000 - Incoming(null));
        int? deadline = null;
        long defense = Defense(target, departing);
        foreach (var arrival in targetArrivals.Where(a => a.Hostile && a.Estimate.Status == ArrivalEstimateStatus.Known)
            .OrderBy(a => a.Estimate.Days).ThenBy(a => a.UnitId))
        {
            power += arrival.EffectiveStrength;
            uint count = (uint)Math.Min(uint.MaxValue, (power * 1000 + defense - 1) / defense);
            uint remaining = (uint)Math.Max(0, count - Incoming(arrival.Estimate.Days));
            required = Math.Max(required, remaining);
            if (remaining > target.Soldiers && deadline is null) deadline = arrival.Estimate.Days;
        }
        return needs[(target.Id, departing)] = (required, deadline);
    }
}
