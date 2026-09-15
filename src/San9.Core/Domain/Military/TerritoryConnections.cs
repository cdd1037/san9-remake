using System.Collections.Immutable;

namespace San9.Core.Domain;

[Flags]
public enum TerritoryExits { None = 0, North = 1, East = 2, South = 4, West = 8, All = 15 }
public sealed record TerritoryConnection(int FromRegionId, int ToRegionId);

public sealed partial class MilitaryRulebook
{
    // Only immutable map inputs are cached. Harbor existence and ownership are queried from the current world.
    private readonly ImmutableArray<TerritoryBorder> territoryBorders;
    private sealed record TerritoryBorder(int From, int To, bool FromNeedsHarbor, bool ToNeedsHarbor);

    private ImmutableArray<TerritoryBorder> BuildTerritoryBorders(ImmutableArray<TerritoryExits> exits)
    {
        if (exits.IsDefault || !exits.IsEmpty && (exits.Length != RegionIds.Length || exits.Length != Map.Width * Map.Height) ||
            exits.Any(e => (e & ~TerritoryExits.All) != 0))
            throw new ArgumentException("Invalid territory exits.");
        HashSet<TerritoryBorder> borders = [];
        bool Eligible(int index) => RegionIds[index] is not null &&
            !geographicBlocked.Contains(new(index % Map.Width, index / Map.Width));
        bool Water(int index) => Map.IsWater(MapCoordinates.Center(new(index % Map.Width, index / Map.Width)));
        for (int index = 0; index < exits.Length; index++)
        {
            if (!Eligible(index)) continue;
            int x = index % Map.Width, y = index / Map.Width;
            void Add(int destination, TerritoryExits direction)
            {
                if ((exits[index] & direction) == 0 || !Eligible(destination) || RegionIds[index] == RegionIds[destination]) return;
                borders.Add(new(RegionIds[index]!.Value, RegionIds[destination]!.Value, Water(index), Water(destination)));
            }
            if (y > 0) Add(index - Map.Width, TerritoryExits.North);
            if (x + 1 < Map.Width) Add(index + 1, TerritoryExits.East);
            if (y + 1 < Map.Height) Add(index + Map.Width, TerritoryExits.South);
            if (x > 0) Add(index - 1, TerritoryExits.West);
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
