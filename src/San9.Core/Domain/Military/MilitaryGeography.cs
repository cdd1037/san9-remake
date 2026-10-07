namespace San9.Core.Domain;

public sealed partial class MilitaryRulebook
{
    public IReadOnlySet<MapCell> GeographicFootprint(SettlementState settlement) =>
        GeographicCells(SettlementArea(settlement)).ToHashSet();

    public IReadOnlySet<MapCell> GeographicFootprint(UnitState unit)
    {
        var shape = Formation(unit).Footprint;
        return GeographicCells(new(unit.Position, new(checked(shape.Width * Geography.FootprintScale),
            checked(shape.Height * Geography.FootprintScale)))).ToHashSet();
    }

    /// <summary>The source city's influence, independent of the owner's other cities.</summary>
    public IReadOnlySet<MapCell> CityInfluenceCells(SettlementState source, IEnumerable<SettlementState> settlements)
    {
        if (CityInfluence is not { } rule || source.Kind != SettlementKind.City || source.IsDestroyed ||
            source.Facility?.BuilderUnitId is not null || !TryPlace(source, out _)) return new HashSet<MapCell>();
        var visited = GeographicFootprint(source).ToHashSet();
        var occupied = settlements.Where(s => s.Id != source.Id && !s.IsDestroyed && s.Facility?.BuilderUnitId is null && TryPlace(s, out _))
            .Select(SettlementArea).ToArray();
        var queue = new Queue<(MapCell Cell, int Distance)>();
        foreach (var seed in visited) queue.Enqueue((seed, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Distance >= rule.RadiusCells) continue;
            foreach (var next in new MapCell[] { new(current.Cell.X, current.Cell.Y - 1), new(current.Cell.X + 1, current.Cell.Y),
                new(current.Cell.X, current.Cell.Y + 1), new(current.Cell.X - 1, current.Cell.Y) })
            {
                if (next.X < 0 || next.Y < 0 || next.X >= Map.Width || next.Y >= Map.Height || visited.Contains(next) ||
                    geographicBlocked.Contains(next) || occupied.Any(area => Inside(next, area))) continue;
                visited.Add(next);
                queue.Enqueue((next, current.Distance + 1));
            }
        }
        return visited;
    }

    /// <summary>Canonical fine-coordinate building rectangle for combat, navigation and map coverage.</summary>
    public MapOccupancy SettlementArea(SettlementState settlement) => BuildingArea(Place(settlement));

    public MapOccupancy BuildingArea(BattlefieldPlace place) => ScaledBuildingArea(place.Position, place.Footprint);
    public MapOccupancy FacilityArea(WorldPosition position, int typeId) => ScaledBuildingArea(position, Facilities[typeId].Footprint);
    private MapOccupancy ScaledBuildingArea(WorldPosition position, Footprint shape) =>
        new(position, new(checked(shape.Width * Geography.FootprintScale), checked(shape.Height * Geography.FootprintScale)));

    private static bool Inside(MapCell cell, MapOccupancy area)
    {
        var center = MapCoordinates.Center(cell);
        return center.X >= area.Left && center.X < area.Right && center.Y >= area.Top && center.Y < area.Bottom;
    }

    public IEnumerable<MapCell> GeographicCells(MapOccupancy area)
    {
        int scale = MapCoordinates.UnitsPerCell;
        for (long y = Math.Max(0, (area.Top + scale / 2) / scale); y <= Math.Min(Map.Height - 1, (area.Bottom - 1) / scale); y++)
            for (long x = Math.Max(0, (area.Left + scale / 2) / scale); x <= Math.Min(Map.Width - 1, (area.Right - 1) / scale); x++)
            {
                var cell = new MapCell((int)x, (int)y);
                if (Inside(cell, area)) yield return cell;
            }
    }

    public bool RockfallCovers(SettlementState source, WorldPosition target, IEnumerable<SettlementState> settlements)
        => RockfallPath(source, target, settlements).Count > 0;

    public IReadOnlyList<MapCell> RockfallPath(SettlementState source, WorldPosition target, IEnumerable<SettlementState> settlements)
    {
        if (Rockfall is not { } profile || source.IsDestroyed || source.Facility?.BuilderUnitId is not null ||
            !TryPlace(source, out _) || !FacilityAttack(source).UsesRockfallPower ||
            target.X < 0 || target.Y < 0 || target.X >= Map.FineWidth || target.Y >= Map.FineHeight) return [];
        var destination = MapCoordinates.Cell(target);
        var visited = GeographicCells(SettlementArea(source)).ToHashSet();
        if (visited.Contains(destination)) return [];
        var predecessors = new Dictionary<MapCell, MapCell>();
        var occupied = settlements.Where(s => s.Id != source.Id && !s.IsDestroyed && TryPlace(s, out _))
            .Select(SettlementArea).ToArray();
        var queue = new Queue<(MapCell Cell, int Distance)>();
        foreach (var seed in visited) queue.Enqueue((seed, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Distance >= profile.PropagationSteps || current.Distance > 0 && Map.StopsRockfall(current.Cell)) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var next = new MapCell(current.Cell.X + dx, current.Cell.Y + dy);
                    if (dx == 0 && dy == 0 || next.X < 0 || next.Y < 0 || next.X >= Map.Width || next.Y >= Map.Height ||
                        visited.Contains(next) || geographicBlocked.Contains(next) || Map.IsWater(MapCoordinates.Center(next)) ||
                        elevations[next.Y * Map.Width + next.X] > elevations[current.Cell.Y * Map.Width + current.Cell.X] ||
                        occupied.Any(area => Inside(next, area))) continue;
                    predecessors[next] = current.Cell;
                    if (next == destination)
                    {
                        List<MapCell> path = [next];
                        while (predecessors.TryGetValue(path[^1], out var previous)) path.Add(previous);
                        path.Reverse();
                        return path;
                    }
                    visited.Add(next);
                    queue.Enqueue((next, current.Distance + 1));
                }
        }
        return [];
    }

    public int FacilityAttackRange(SettlementState source, WorldPosition target, IEnumerable<SettlementState> settlements) =>
        RockfallCovers(source, target, settlements) ? Rockfall!.AttackRange :
        AttackRange(FacilityAttack(source).Attack, Place(source).Position, target, false, false);
}
