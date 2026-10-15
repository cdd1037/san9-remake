using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed record PublicMilitaryForce(int? UnitId, int? SettlementId, WorldPosition Position,
    UnitTravel Travel, uint Soldiers, long Strength, int PreparationDays, int AttackRange)
{
    internal int OwnerId { get; init; }
}
internal sealed record ThreatArrival(PublicMilitaryForce Source, int TargetId, int EarliestDays);

/// <summary>Sparse public influence projection. Each source belongs to one location in a joint hypothesis.
/// Arrival bounds deliberately overestimate enemy mobility; they never certify an own route.</summary>
internal sealed class MilitarySituation(ImmutableArray<PublicMilitaryForce> sources,
    ImmutableDictionary<int, ImmutableHashSet<int>> enemies)
{
    internal ImmutableArray<PublicMilitaryForce> Sources { get; } = sources;

    internal static MilitarySituation Capture(WorldSnapshot world, int observer, MilitaryRulebook rules)
    {
        bool Enemy(int owner) => owner != observer && MilitaryDiplomacy.AutomaticEnemy(world, observer, owner);
        var people = world.Officers.Where(p => p.IsVisible).ToLookup(p => p.UnitId);
        var sources = ImmutableArray.CreateBuilder<PublicMilitaryForce>();
        foreach (var unit in world.Units.Where(u => Enemy(u.OwnerId) && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Attack is not null).OrderBy(u => u.Id))
        {
            var formation = rules.Formation(unit);
            int leadership = people[unit.Id].Select(p => p.BaseAbilities.Leadership).DefaultIfEmpty(50).Max();
            long strength = formation.Combat is { } profile
                ? CombatMath.FormationStrength(new(unit.Soldiers, unit.Morale, leadership, 100, 100), profile).AgainstGarrison * unit.Soldiers / 1000
                : CombatMath.PolicyStrength(new(unit.Soldiers, unit.Morale, leadership, 100, 100));
            int Range(FormationRule f) => f.Attack?.RangedSiege == true ? checked(f.Attack.Range + f.Attack.MaximumElevationBonus) : 1;
            int range = Math.Max(Range(formation), unit.NavalFormationId is int naval ? Range(rules.Formations[naval]) : 1);
            sources.Add(new(unit.Id, null, unit.Position, rules.Travel(unit), unit.Soldiers, strength, 0, range) { OwnerId = unit.OwnerId });
        }
        var common = rules.Formations.Values.Where(f => f.IsCommon && f.RequiredTacticId is null && f.Combat is not null && f.Attack is not null &&
                rules.ValidUnitFormations(f.Id, rules.NavalFormationForDeployment(f.Id)))
            .OrderByDescending(f => f.MovementBudget).ThenBy(f => f.Id).FirstOrDefault();
        if (common is not null)
            foreach (var place in world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.CanHostPersons &&
                s.OwnerId is int owner && Enemy(owner) && s.Soldiers > 1000 && rules.TryPlace(s, out var p) && p.Combat is not null).OrderBy(s => s.Id))
            {
                uint soldiers = Math.Min(place.Soldiers - 1000, common.SoldierCapacity);
                int leadership = world.Officers.Where(p => p.IsVisible && p.SettlementId == place.Id && p.OwnerId == place.OwnerId)
                    .Select(p => p.BaseAbilities.Leadership).DefaultIfEmpty(50).Max();
                long strength = CombatMath.FormationStrength(new(soldiers, place.Morale, leadership, 100, 100), common.Combat!).AgainstGarrison * soldiers / 1000;
                sources.Add(new(null, place.Id, rules.Place(place).Position,
                    rules.Travel(common.Id, rules.NavalFormationForDeployment(common.Id)),
                    soldiers, strength, 10, 1) { OwnerId = place.OwnerId!.Value });
            }
        return new(sources.ToImmutable(), sources.Select(s => s.OwnerId).Distinct().ToImmutableDictionary(owner => owner,
            owner => world.Forces.Where(f => MilitaryDiplomacy.AutomaticEnemy(world, owner, f.Id)).Select(f => f.Id).ToImmutableHashSet()));
    }

    internal (ImmutableArray<ThreatArrival> Arrivals, bool Complete) Project(
        IReadOnlyList<SettlementState> targets, MilitaryRulebook rules, AllocationWork work, bool includeGarrisons, bool attackRange)
    {
        var result = ImmutableArray.CreateBuilder<ThreatArrival>();
        using var steps = ProjectSteps(targets, rules, includeGarrisons, attackRange, result.Add).GetEnumerator();
        bool pending = steps.MoveNext();
        while (pending && work.Spend())
        {
            steps.Current?.Invoke();
            pending = steps.MoveNext();
        }
        return (result.ToImmutable(), !pending);
    }

    internal IEnumerable<Action?> ProjectSteps(IReadOnlyList<SettlementState> targets, MilitaryRulebook rules,
        bool includeGarrisons, bool attackRange, Action<ThreatArrival> accept)
    {
        foreach (var source in Sources.Where(s => includeGarrisons || s.UnitId is not null))
        {
            yield return null;
            int? targetId = null;
            int earliest = int.MaxValue;
            foreach (var target in targets)
            {
                if (target.Id == source.SettlementId || target.OwnerId is int owner && !enemies[source.OwnerId].Contains(owner)) continue;
                yield return () =>
                {
                    int days = NavigationArrivalForecast.EarliestDays(source.Travel, source.Position, rules.SettlementArea(target),
                        source.PreparationDays, movementRemainder: source.UnitId is null ? 0 : null,
                        maximumRange: attackRange ? source.AttackRange : 1);
                    if (targetId is null || days < earliest || days == earliest && target.Id < targetId)
                    { targetId = target.Id; earliest = days; }
                };
            }
            if (targetId is int id) accept(new(source, id, earliest));
        }
    }
}
