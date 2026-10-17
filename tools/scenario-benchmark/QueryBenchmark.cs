using System.Diagnostics;
using San9.Core.Domain;
using San9.Core.Simulation;

internal static class QueryBenchmark
{
    internal static void Run(WorldSnapshot world, MilitaryRulebook rules, Action<string> log)
    {
        var units = world.Units.Where(u => u.EffectiveOrder is not null).ToArray();
        foreach (string kind in new[] { "preview", "ai-estimate", "arrival" })
        for (int repeat = 0; repeat < 6; repeat++)
        {
            var context = new MilitaryRouteQueryContext(world, rules);
            var arrival = new MilitaryArrivalForecast(world, rules);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            using var diagnostics = new PathSearchDiagnostics();
            var timings = new List<double>();
            var results = new Dictionary<string, int>();
            var clock = Stopwatch.StartNew();
            foreach (var unit in units)
            {
                long started = Stopwatch.GetTimestamp();
                string result = kind switch
                {
                    "preview" => context.Plan(unit, unit.Id).Status.ToString(),
                    "ai-estimate" => context.Estimate(unit, unit.Id).Status.ToString(),
                    _ => arrival.Estimate(unit.Id).Status.ToString()
                };
                timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                results[result] = results.GetValueOrDefault(result) + 1;
            }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            timings.Sort();
            string p95 = timings.Count == 0 ? "N/A" : $"{timings[(int)(timings.Count * .95)]:F3}";
            log($"query={kind} repeat={repeat + 1} count={units.Length} ms={elapsed:F2} p95_ms={p95} " +
                $"allocated_MiB={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0:F2} searches={diagnostics.Queries} " +
                $"expanded={diagnostics.Expanded} outcomes={string.Join(',', results.Select(p => $"{p.Key}:{p.Value}"))}");
        }
    }
}
