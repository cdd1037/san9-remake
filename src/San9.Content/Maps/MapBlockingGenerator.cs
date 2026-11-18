using San9.Content.Packages;
using San9.Core.Domain;

namespace San9.Content.Maps;

/// <summary>Fills authored blocking values on demand, preserving inputs outside the affected area.</summary>
public static class MapBlockingGenerator
{
    public static MapCellDefinition[] Generate(MapDefinition map, IEnumerable<MapCell> harbors,
        IEnumerable<int>? selectedIndices = null, bool resolveLayout = false)
    {
        map.Validate();
        var selected = selectedIndices?.ToHashSet() ?? Enumerable.Range(0, map.Cells.Length).ToHashSet();
        if (selected.Any(index => index < 0 || index >= map.Cells.Length))
            throw new ArgumentOutOfRangeException(nameof(selectedIndices));
        var ports = harbors.Distinct().ToArray();
        if (ports.Any(p => p.X < 0 || p.Y < 0 || p.X >= map.Width || p.Y >= map.Height))
            throw new ArgumentOutOfRangeException(nameof(harbors));
        var cells = (MapCellDefinition[])map.Cells.Clone();
        var blockedTerrains = map.BlockingDefaults.InfluenceBlockedTerrainIds.ToHashSet();
        var harborAccess = HarborShoreCells(map, ports).ToHashSet();
        foreach (int index in selected)
        {
            var cell = cells[index];
            if (!resolveLayout || cell.InfluenceSource != BlockingSource.Manual)
                cells[index] = cell with { InfluenceBlocked = blockedTerrains.Contains(cell.Terrain) && !harborAccess.Contains(index),
                    InfluenceSource = resolveLayout ? cell.InfluenceSource : BlockingSource.TerrainRule };
        }

        var affected = selected.SelectMany(index => Neighborhood(index % map.Width, index / map.Width))
            .Where(p => Inside(p.X, p.Y)).Select(p => p.Y * map.Width + p.X).ToHashSet();
        if (resolveLayout)
            affected.UnionWith(Enumerable.Range(0, cells.Length).Where(i => cells[i].MovementSource == BlockingSource.TerrainRule));
        foreach (int index in affected)
        {
            if (resolveLayout && cells[index].MovementSource == BlockingSource.Manual) continue;
            bool blocked = Neighborhood(index % map.Width, index / map.Width)
                .Any(p => !Inside(p.X, p.Y) || cells[p.Y * map.Width + p.X].InfluenceBlocked);
            cells[index] = cells[index] with { MovementBlocked = blocked,
                MovementSource = resolveLayout ? cells[index].MovementSource : BlockingSource.TerrainRule };
        }
        return cells;

        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height;
    }

    public static IEnumerable<int> HarborShoreCells(MapDefinition map, IEnumerable<MapCell> harbors)
    {
        var terrains = map.BlockingDefaults.HarborAccessTerrainIds.ToHashSet();
        int radius = map.BlockingDefaults.HarborAccessRadius;
        foreach (var harbor in harbors)
        for (int y = (int)Math.Max(0L, (long)harbor.Y - radius); y <= Math.Min(map.Height - 1L, (long)harbor.Y + radius); y++)
        for (int x = (int)Math.Max(0L, (long)harbor.X - radius); x <= Math.Min(map.Width - 1L, (long)harbor.X + radius); x++)
        {
            int index = y * map.Width + x;
            if (terrains.Contains(map.Cells[index].Terrain)) yield return index;
        }
    }

    private static IEnumerable<MapCell> Neighborhood(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                yield return new(x + dx, y + dy);
    }
}
