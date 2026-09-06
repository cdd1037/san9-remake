namespace San9.Application.Presentation;

public enum OverviewLayer { Forces, Facilities, Units }
public enum ForceRankingMetric { Trust, Cities, Officers, Soldiers }
public sealed record ForceRankingEntry(int Rank, ForceView Force, long Value);
public sealed record ForceRankingColumn(ForceRankingMetric Metric, IReadOnlyList<ForceRankingEntry> Leaders, ForceRankingEntry? Player);

public sealed class StrategyOverview
{
    public bool IsOpen { get; private set; }
    public bool ShowForces { get; private set; } = true;
    public bool ShowFacilities { get; private set; } = true;
    public bool ShowUnits { get; private set; } = true;
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Toggle(OverviewLayer layer)
    {
        switch (layer)
        {
            case OverviewLayer.Forces: ShowForces = !ShowForces; break;
            case OverviewLayer.Facilities: ShowFacilities = !ShowFacilities; break;
            case OverviewLayer.Units: ShowUnits = !ShowUnits; break;
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    public static IReadOnlyList<ForceRankingColumn> Rankings(StrategySnapshot snapshot)
    {
        var settlements = snapshot.Settlements.Where(s => s.OwnerId is not null).ToLookup(s => s.OwnerId!.Value);
        var officers = snapshot.Officers.Where(o => o.IsServing && o.OwnerId is not null).ToLookup(o => o.OwnerId!.Value);
        var units = snapshot.World.Units.ToLookup(u => u.OwnerId);
        var forces = snapshot.Forces.Where(f => !f.IsBarbarian || f.Id == snapshot.PlayerForceId).ToArray();
        return Array.AsReadOnly(Enum.GetValues<ForceRankingMetric>().Select(metric =>
        {
            long Value(ForceView force) => metric switch
            {
                ForceRankingMetric.Trust => force.Trust,
                ForceRankingMetric.Cities => settlements[force.Id].Count(s => s.Kind == San9.Core.Domain.SettlementKind.City),
                ForceRankingMetric.Officers => officers[force.Id].LongCount(),
                ForceRankingMetric.Soldiers => settlements[force.Id].Sum(s => (long)s.Soldiers) + units[force.Id].Sum(u => (long)u.Soldiers),
                _ => throw new ArgumentOutOfRangeException(nameof(metric))
            };
            int rank = 0; long? previous = null;
            var entries = forces.Select(f => (Force: f, Value: Value(f))).OrderByDescending(f => f.Value).ThenBy(f => f.Force.Id)
                .Select((f, index) =>
                {
                    if (previous != f.Value) rank = index + 1;
                    previous = f.Value;
                    return new ForceRankingEntry(rank, f.Force, f.Value);
                }).ToArray();
            var leaders = entries.Take(3).ToArray();
            return new ForceRankingColumn(metric, Array.AsReadOnly(leaders),
                leaders.Any(e => e.Force.Id == snapshot.PlayerForceId) ? null : entries.SingleOrDefault(e => e.Force.Id == snapshot.PlayerForceId));
        }).ToArray());
    }
}
