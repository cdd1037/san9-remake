using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record TerritoryConnection(int FromRegionId, int ToRegionId);

public sealed partial class MilitaryRulebook
{
    // Only immutable map inputs are cached. Harbor existence and ownership are queried from the current world.
    private readonly ImmutableArray<TerritoryBorder> territoryBorders;
    private sealed record TerritoryBorder(int From, int To, bool FromNeedsHarbor, bool ToNeedsHarbor);

    private ImmutableArray<TerritoryBorder> BuildTerritoryBorders()
    {
        HashSet<TerritoryBorder> borders = [];
        bool Eligible(int index) => RegionIds[index] is not null &&
            !geographicBlocked.Contains(new(index % Map.Width, index / Map.Width));
        bool Water(int index) => Map.IsWater(MapCoordinates.Center(new(index % Map.Width, index / Map.Width)));
        for (int index = 0; index < RegionIds.Length; index++)
        {
            if (!Eligible(index)) continue;
            int x = index % Map.Width, y = index / Map.Width;
            void Add(int destination)
            {
                if (!Eligible(destination) || RegionIds[index] == RegionIds[destination]) return;
                borders.Add(new(RegionIds[index]!.Value, RegionIds[destination]!.Value, Water(index), Water(destination)));
            }
            if (y > 0) Add(index - Map.Width);
            if (x + 1 < Map.Width) Add(index + 1);
            if (y + 1 < Map.Height) Add(index + Map.Width);
            if (x > 0) Add(index - 1);
        }
        return borders.OrderBy(b => b.From).ThenBy(b => b.To).ThenBy(b => b.FromNeedsHarbor).ThenBy(b => b.ToNeedsHarbor).ToImmutableArray();
    }

    public ImmutableArray<TerritoryConnection> TerritoryConnections(IEnumerable<SettlementState> settlements)
    {
        var harbors = settlements.Where(IsHarbor).Select(s => RegionAt(Place(s).Position)).OfType<int>().ToHashSet();
        return territoryBorders.Where(b => (!b.FromNeedsHarbor || harbors.Contains(b.From)) &&
                (!b.ToNeedsHarbor || harbors.Contains(b.To)))
            .Select(b => new TerritoryConnection(b.From, b.To)).Distinct().ToImmutableArray();
    }
}
