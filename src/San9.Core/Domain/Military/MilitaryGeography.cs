namespace San9.Core.Domain;

public sealed partial class MilitaryRulebook
{
    private MapOccupancy GeographicArea(SettlementState settlement)
    {
        var place = Place(settlement);
        return new(place.Position, new(checked(place.Footprint.Width * Geography.FootprintScale),
            checked(place.Footprint.Height * Geography.FootprintScale)));
    }

    private static bool Inside(MapCell cell, MapOccupancy area)
    {
        var center = MapCoordinates.Center(cell);
        return center.X >= area.Left && center.X < area.Right && center.Y >= area.Top && center.Y < area.Bottom;
    }

    private IEnumerable<MapCell> GeographicCells(MapOccupancy area)
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
    {
        if (Rockfall is not { } profile || source.IsDestroyed || source.Facility?.BuilderUnitId is not null ||
            !TryPlace(source, out _) || !FacilityAttack(source).UsesRockfallPower ||
            target.X < 0 || target.Y < 0 || target.X >= Map.FineWidth || target.Y >= Map.FineHeight) return false;
        var destination = MapCoordinates.Cell(target);
        var visited = GeographicCells(GeographicArea(source)).ToHashSet();
        if (visited.Contains(destination)) return false;
        var occupied = settlements.Where(s => s.Id != source.Id && !s.IsDestroyed && TryPlace(s, out _))
            .Select(GeographicArea).ToArray();
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
                    if (next == destination) return true;
                    visited.Add(next);
                    queue.Enqueue((next, current.Distance + 1));
                }
        }
        return false;
    }

    public int FacilityAttackRange(SettlementState source, WorldPosition target, IEnumerable<SettlementState> settlements) =>
        RockfallCovers(source, target, settlements) ? Rockfall!.AttackRange :
        AttackRange(FacilityAttack(source).Attack, Place(source).Position, target, false, false);
}
