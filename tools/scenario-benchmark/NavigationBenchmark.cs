using System.Diagnostics;
using San9.Content.Scenarios;
using San9.Core.Domain;

internal static class NavigationBenchmark
{
    internal static void Quality(ScenarioInitialState definition, Action<string> log)
    {
        var rules = new MilitaryRulebook(definition.Military!);
        foreach (var unit in definition.Units)
        {
            var formation = rules.Formations[unit.FormationId!.Value];
            var place = rules.Places[unit.Order!.SettlementId!.Value];
            var target = new MapOccupancy(place.Position, place.Footprint);
            var objects = rules.Places.Values.Where(p => p.Id != place.Id).Select(p => new MapOccupancy(p.Position, p.Footprint))
                .Concat(definition.Units.Where(u => u.Id != unit.Id).Select(u => new MapOccupancy(u.Position, formation.Footprint))).ToArray();
            var position = unit.Position;
            MovementPlan? plan = null;
            long cost = 0;
            bool arrived = false;
            int days = 0;
            for (; days < 2000 && !arrived; days++)
            {
                var step = rules.Map.Navigate(position, target, true, formation.Footprint, formation.TravelMode,
                    area => objects.Where(o => o.Overlaps(area)).ToArray(), formation.MovementBudget, plan, maximumRange: 10);
                position = step.Position; plan = step.Plan; cost += step.Spent; arrived = step.Arrived;
                if (step.Status == NavigationStatus.Unreachable) break;
            }
            log($"route={unit.Id} reachable={arrived} cost={cost} days={days}");
        }
    }

    internal static void Run(ScenarioInitialState definition, int xuns, Action<string> log, bool fullRoute)
    {
        var rules = new MilitaryRulebook(definition.Military!);
        var units = definition.Units.ToArray();
        var starts = units.Select(u => u.Position).ToArray();
        var returning = new bool[units.Length];
        var places = rules.Places.Values.Select(p => new MapOccupancy(p.Position, p.Footprint)).ToArray();
        log($"navigation_only units={units.Length} days={xuns * 10} all_facilities_block=true combat=false full_route={fullRoute}");
        for (int xun = 0; xun < xuns; xun++)
        {
            using var diagnostics = new PathSearchDiagnostics();
            int moved = 0, waiting = 0, arrived = 0;
            var clock = Stopwatch.StartNew();
            for (int day = 0; day < 10; day++)
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                var formation = rules.Formations[unit.FormationId!.Value];
                var place = rules.Places[unit.Order!.SettlementId!.Value];
                var target = returning[i] ? new MapOccupancy(starts[i], new(1, 1)) : new MapOccupancy(place.Position, place.Footprint);
                NavigationStep step;
                if (fullRoute)
                {
                    var occupancy = places.Concat(units.Where(u => u.Id != unit.Id).Select(u =>
                        new MapOccupancy(u.Position, rules.Formations[u.FormationId!.Value].Footprint))).ToArray();
                    var move = returning[i]
                        ? rules.Map.Advance(unit.Position, target.Position, formation.Footprint, formation.TravelMode, occupancy,
                            formation.MovementBudget, formation.MovementStepCost)
                        : rules.Map.AdvanceIntoRange(unit.Position, target, formation.Footprint, formation.TravelMode, occupancy,
                            formation.MovementBudget, 10, formation.MovementStepCost);
                    step = new(move.Position, move.Spent, move.Arrived ? NavigationStatus.Arrived :
                        move.Reachable ? NavigationStatus.Moving : NavigationStatus.Unreachable, null);
                }
                else step = rules.Map.Navigate(unit.Position, target, !returning[i], formation.Footprint, formation.TravelMode,
                    envelope => places.Concat(units.Where(u => u.Id != unit.Id).Select(u =>
                        new MapOccupancy(u.Position, rules.Formations[u.FormationId!.Value].Footprint))).Where(a => a.Overlaps(envelope)).ToArray(),
                    formation.MovementBudget, unit.MovementPlan, formation.MovementStepCost, maximumRange: 10);
                if (step.Position != unit.Position) moved++;
                if (step.Status == NavigationStatus.Waiting) waiting++;
                if (step.Arrived) { arrived++; returning[i] = !returning[i]; }
                units[i] = unit with { Position = step.Position, MovementPlan = step.Plan };
            }
            log($"navigation_xun={xun + 1} ms={clock.Elapsed.TotalMilliseconds:F2} units={units.Length} moved={moved} waiting={waiting} arrivals={arrived} searches={diagnostics.Queries} expanded={diagnostics.Expanded} reused={diagnostics.ReusedPlans}");
        }
    }
}
