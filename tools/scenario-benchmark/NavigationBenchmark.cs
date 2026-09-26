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
            var travel = rules.Travel(unit);
            var place = rules.Places[unit.Order!.SettlementId!.Value];
            var target = new MapOccupancy(place.Position, place.Footprint);
            var objects = rules.Places.Values.Where(p => p.Id != place.Id).Select(p => new MapOccupancy(p.Position, p.Footprint))
                .Concat(definition.Units.Where(u => u.Id != unit.Id).Select(u => new MapOccupancy(u.Position, rules.Formation(u).Footprint))).ToArray();
            var position = unit.Position;
            MovementPlan? plan = unit.MovementPlan;
            int remainder = unit.MovementRemainder;
            long cost = 0;
            bool arrived = false;
            int days = 0;
            for (; days < 2000 && !arrived; days++)
            {
                var step = travel.Map.Navigate(position, target, true, travel.Shape, travel.Mode,
                    area => objects.Where(o => o.Overlaps(area)).ToArray(), checked(travel.DailyBudget + remainder), plan, maximumRange: 10);
                position = step.Position; plan = step.Plan; remainder = step.Remainder; cost += step.Spent; arrived = step.Arrived;
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
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            using var diagnostics = new PathSearchDiagnostics();
            int moved = 0, waiting = 0, arrived = 0;
            var clock = Stopwatch.StartNew();
            for (int day = 0; day < 10; day++)
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                var travel = rules.Travel(unit);
                int budget = checked(travel.DailyBudget + unit.MovementRemainder);
                var place = rules.Places[unit.Order!.SettlementId!.Value];
                var target = returning[i] ? new MapOccupancy(starts[i], new(1, 1)) : new MapOccupancy(place.Position, place.Footprint);
                NavigationStep step;
                if (fullRoute)
                {
                    var occupancy = places.Concat(units.Where(u => u.Id != unit.Id).Select(u =>
                        new MapOccupancy(u.Position, rules.Formation(u).Footprint))).ToArray();
                    var move = returning[i]
                        ? travel.Map.Advance(unit.Position, target.Position, travel.Shape, travel.Mode, occupancy, budget)
                        : travel.Map.AdvanceIntoRange(unit.Position, target, travel.Shape, travel.Mode, occupancy, budget, 10);
                    step = new(move.Position, move.Spent, move.Arrived ? NavigationStatus.Arrived :
                        move.Reachable ? NavigationStatus.Moving : NavigationStatus.Unreachable, null)
                    { Remainder = move.Reachable && !move.Arrived ? budget - move.Spent : 0 };
                }
                else step = travel.Map.Navigate(unit.Position, target, !returning[i], travel.Shape, travel.Mode,
                    envelope => places.Concat(units.Where(u => u.Id != unit.Id).Select(u =>
                        new MapOccupancy(u.Position, rules.Formation(u).Footprint))).Where(a => a.Overlaps(envelope)).ToArray(),
                    budget, unit.MovementPlan, maximumRange: 10);
                if (step.Position != unit.Position) moved++;
                if (step.Status == NavigationStatus.Waiting) waiting++;
                if (step.Arrived) { arrived++; returning[i] = !returning[i]; }
                units[i] = unit with { Position = step.Position, MovementPlan = step.Plan, MovementRemainder = step.Remainder };
            }
            log($"navigation_xun={xun + 1} ms={clock.Elapsed.TotalMilliseconds:F2} units={units.Length} moved={moved} waiting={waiting} arrivals={arrived} searches={diagnostics.Queries} expanded={diagnostics.Expanded} reused={diagnostics.ReusedPlans} " +
                $"topology_builds={diagnostics.TopologyBuilds} workspace_growths={diagnostics.WorkspaceGrowths} allocated_MiB={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0:F2}");
        }
    }
}
