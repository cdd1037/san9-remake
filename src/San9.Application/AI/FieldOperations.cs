using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed record FieldObservation(ImmutableArray<AllocationCandidate> Candidates, bool Complete);

/// <summary>Public field opportunities and recovery, at the same command boundary as campaign allocation.</summary>
internal static class FieldOperations
{
    internal static FieldObservation Capture(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        MilitaryRulebook rules, AllocationWork work, ArrivalProjectionCache routes)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var members = world.Officers.Where(p => p.IsServing).ToLookup(p => p.UnitId);
        var knownTactics = new KnownTactics(engine, world, seat.ForceId, rules);
        var enemies = world.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null &&
            rules.Formation(u).Combat is not null && MilitaryDiplomacy.AutomaticEnemy(world, seat.ForceId, u.OwnerId)).OrderBy(u => u.Id).ToArray();
        var result = ImmutableArray.CreateBuilder<AllocationCandidate>();
        HashSet<int> intercepted = [];
        bool allComplete = true;
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Combat is not null).OrderBy(u => u.Id))
        {
            if (!work.Spend()) { allComplete = false; break; }
            var scope = economies.FirstOrDefault(e => CorpsControl.CanControlUnit(world, seat, new(e.ControllerId), unit.Id));
            if (scope.Observation is null) continue;
            int account = unit.CorpsId ?? world.Corps.Single(c => c.OwnerId == seat.ForceId && c.IsRoot).Id;
            var crew = members[unit.Id].Where(p => p.OwnerId == seat.ForceId).ToImmutableArray();
            if (crew.IsEmpty) continue;
            var formation = rules.Formation(unit);
            var strength = new CombatStrength(unit.Soldiers, unit.Morale, crew.Max(p => abilities[p.Id].Leadership), 1, 10);
            var occupancy = MilitaryRouteQuery.Occupancy(world, rules, seat.ForceId, unit.Policies, includeUnits: false).ToImmutableArray();
            ArrivalEstimate Route(MapOccupancy target, bool returning = false) => routes.Evaluate(new(rules.Travel(unit), unit.Position,
                target, occupancy, PreparationDays: unit.Condition == UnitCondition.Confused ? unit.ConditionDaysRemaining : 0,
                MovementRemainder: unit.MovementRemainder, AllowTargetOverlap: returning), work, sharedTerrain: true);
            bool Legal(GameCommand command) => work.Spend() && engine.GetPreparationEligibility(new(scope.ControllerId), command with
                { CommandId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Sequence = world.Simulation.NextSequence }) == CommandRejection.None;
            AllocationCandidate Candidate(GameCommand command, long value, int priority, ArrivalEstimate? arrival = null, int? home = null) =>
                new(scope.ControllerId, account, command, [], priority, value, 0, 0, unit.ServicePlaceId, home, arrival,
                    Purpose: $"field:{unit.Id}");

            // Embedded troops and personnel journeys have separate owners. Do not cancel construction or transport cargo.
            if (unit.Order?.Kind is MilitaryOrderKind.Construct or MilitaryOrderKind.Transport or MilitaryOrderKind.Return) continue;
            bool recovery = unit.Wounded > unit.Soldiers || crew.All(p => p.Injury > 0) || unit.Morale < 30;
            AllocationCandidate? best = null;
            bool complete = true;
            if (!recovery && formation.Attack is not null)
            foreach (var enemy in enemies.Where(e => !intercepted.Contains(e.Id))
                .OrderBy(e => MapCoordinates.Distance(unit.Position, e.Position)).ThenBy(e => e.Id))
            {
                if (!work.Spend()) { complete = false; break; }
                var area = new MapOccupancy(enemy.Position, rules.Travel(enemy).Shape);
                int earliest = NavigationArrivalForecast.EarliestDays(rules.Travel(unit), unit.Position, area);
                // Enemy orders are not observed. A distant mobile target is not assumed to wait for us.
                if (earliest > 10 || earliest > 1 && rules.Travel(enemy).DailyBudget >= rules.Travel(unit).DailyBudget) continue;
                if (unit.Order?.Kind == MilitaryOrderKind.AttackSettlement && earliest > 1) continue;
                var order = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = enemy.Id };
                var command = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, order)
                    { Policies = unit.Policies with { Pursue = false, AutonomousRetreat = true } };
                if (!Legal(command)) continue;
                var arrival = Route(area);
                if (arrival.Status == ArrivalEstimateStatus.Incomplete) complete = false;
                if (arrival is not { Status: ArrivalEstimateStatus.Known, Days: <= 10 }) continue;
                int leadership = members[enemy.Id].Where(p => p.IsVisible).Select(p => p.BaseAbilities.Leadership).DefaultIfEmpty(50).Max();
                var target = new CombatStrength(enemy.Soldiers, enemy.Morale, leadership, 1, 10);
                var enemyFormation = rules.Formation(enemy);
                long gain = ExchangeValue(strength, rules.CombatFormation(unit)!, target, rules.CombatFormation(enemy)!, enemyFormation.Attack is not null,
                    arrival.Days!.Value, work);
                if (work.Remaining == 0) complete = false;
                // Nearby enemy reserves can join; count public local pressure without inspecting their orders.
                long pressure = enemies.Where(e => e.Id != enemy.Id &&
                    MapCoordinates.Distance(e.Position, enemy.Position) <= rules.PolicyRadius).Sum(e => (long)e.Soldiers);
                gain -= pressure;
                if (unit.Order?.Kind == MilitaryOrderKind.AttackSettlement) gain -= unit.Soldiers / 2L;
                if (gain <= 0) continue;
                if (best is null || gain > best.Benefit) best = Candidate(command, gain, 2, arrival);
            }
            if (best?.Intent is OrderUnitCommand attack)
            {
                intercepted.Add(attack.Order.UnitId!.Value);
                if (unit.Order != attack.Order) result.Add(best);
                else if (unit.Policies != attack.Policies)
                {
                    var policy = new SetUnitPoliciesCommand(Guid.Empty, seat.ForceId, 0, unit.Id, attack.Policies!);
                    if (Legal(policy)) result.Add(best with { Intent = policy });
                }
            }
            else if (recovery || complete && unit.Order?.Kind == MilitaryOrderKind.AttackUnit)
            {
                // Compare reachable receiving places; a failed or unfinished route never authorizes a withdrawal.
                foreach (var home in world.Settlements.Where(s => s.OwnerId == seat.ForceId && s.CanHostPersons &&
                    !s.IsDormant && !s.IsDestroyed && s.Durability > 0 && rules.TryPlace(s, out _) &&
                    s.SoldierCapacity - s.Soldiers >= unit.Soldiers && MilitaryRulebook.WoundedCapacity - s.Wounded >= unit.Wounded)
                    .OrderBy(s => MapCoordinates.Distance(unit.Position, rules.Place(s).Position)).ThenBy(s => s.Id))
                {
                    if (!work.Spend()) break;
                    var command = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, new(MilitaryOrderKind.Return, null, home.Id))
                        { Policies = unit.Policies with { Pursue = false, AutonomousRetreat = true } };
                    if (!Legal(command)) continue;
                    var arrival = Route(rules.SettlementArea(home), true);
                    if (arrival.Status == ArrivalEstimateStatus.Incomplete) complete = false;
                    if (arrival.Status != ArrivalEstimateStatus.Known) continue;
                    result.Add(Candidate(command, unit.Soldiers + (long)unit.Wounded, 0, arrival, home.Id));
                    break;
                }
            }
            if (!recovery) Equip();
            allComplete &= complete && work.Remaining > 0;

            void Equip()
            {
                bool settlement = unit.Order?.Kind == MilitaryOrderKind.AttackSettlement;
                var available = crew.ToImmutableDictionary(p => p.Id, knownTactics.For);
                var ranks = crew.ToImmutableDictionary(p => p.Id, p => rules.Rank(unit, p.Id));
                var equipped = crew.ToImmutableDictionary(p => p.Id, p => p.EquippedTacticId);
                var automatic = (formation.Tactics?.AutomaticTactics ?? []).Select(id => rules.Tactics[id]).ToImmutableArray();
                long Estimate(ImmutableDictionary<int, int?> choices) => CampaignEquipmentSearch.Estimate(crew, abilities,
                    available, formation, rules.LinkRules, choices, ranks, automatic, settlement).Damage;
                long value = Estimate(equipped);
                AllocationCandidate? improvement = null;
                // One legal improvement at a time preserves the current configuration as a candidate and allows
                // accepted selections to form links. Support/intelligence tactics are not discarded by a martial-only model.
                foreach (var person in crew)
                foreach (var tactic in available[person.Id].Where(t => t.Selectable && t.Effect == TacticEffect.Martial))
                {
                    if (!work.Spend()) { complete = false; break; }
                    if (person.EquippedTacticId is int current && rules.Tactics[current].Effect != TacticEffect.Martial) continue;
                    var choices = equipped.SetItem(person.Id, tactic.Id);
                    long next = Estimate(choices);
                    if (next <= value) continue;
                    var command = new SelectTacticCommand(Guid.Empty, seat.ForceId, 0, person.Id, tactic.Id);
                    if (!Legal(command)) continue;
                    if (improvement is null || next - value > improvement.Benefit)
                        improvement = new(scope.ControllerId, account, command, [], 1, next - value, 0, 0,
                            Purpose: $"field-tactic:{unit.Id}");
                }
                if (improvement is not null) result.Add(improvement);
            }
        }
        return new(result.ToImmutable(), allComplete);
    }

    /// <summary>Bounded simultaneous ordinary exchanges. It values losses and remaining force, not certain kills.</summary>
    internal static long ExchangeValue(CombatStrength own, CombatProfile ownProfile, CombatStrength enemy,
        CombatProfile enemyProfile, bool retaliates, int arrivalDays, AllocationWork work)
    {
        uint originalOwn = own.Soldiers, originalEnemy = enemy.Soldiers;
        for (int day = arrivalDays; day < 20 && own.Soldiers > 0 && enemy.Soldiers > 0; day++)
        {
            if (!work.Spend()) return 0;
            uint dealt = CombatMath.TroopDamage(own, enemy, ownProfile, enemyProfile, false, 0);
            uint taken = retaliates ? CombatMath.TroopDamage(enemy, own, enemyProfile, ownProfile, false, 0) : 0;
            own = own with { Soldiers = own.Soldiers - taken };
            enemy = enemy with { Soldiers = enemy.Soldiers - dealt };
        }
        if (own.Soldiers == 0) return 0;
        return originalEnemy - (long)enemy.Soldiers - 2L * (originalOwn - own.Soldiers);
    }
}
