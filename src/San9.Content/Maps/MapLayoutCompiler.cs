using System.Numerics;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Content.Maps;

/// <summary>Deterministically builds runtime display data from authored cells and a resource theme.</summary>
public static class MapLayoutCompiler
{
    private static readonly (int X, int Y, int Part)[] Directions =
        [(1, 0, 3), (-1, 0, 2), (0, 1, 4), (0, -1, 1), (-1, -1, 5), (1, -1, 6), (1, 1, 8), (-1, 1, 7)];

    public static MapSquare[] Squares(MapDefinition map)
    {
        map.Validate();
        int Height(int x, int y) => x < 0 || y < 0 || x >= map.Width || y >= map.Height
            ? -1 : map.Cells[y * map.Width + x].Elevation;
        return map.Cells.Select((cell, index) =>
        {
            int x = index % map.Width, y = index / map.Width, h = cell.Elevation, mask = 0;
            if (Math.Max(Math.Max(Height(x, y - 1), Height(x - 1, y)), Height(x - 1, y - 1)) > h) mask |= 1;
            if (Math.Max(Math.Max(Height(x, y - 1), Height(x + 1, y)), Height(x + 1, y - 1)) > h) mask |= 8;
            if (Math.Max(Math.Max(Height(x, y + 1), Height(x + 1, y)), Height(x + 1, y + 1)) > h) mask |= 2;
            if (Math.Max(Math.Max(Height(x, y + 1), Height(x - 1, y)), Height(x - 1, y + 1)) > h) mask |= 4;
            return new MapSquare(cell.Terrain, h, mask, cell.RegionId ?? -1)
                { HideRegionName = cell.HideRegionName, TextureVariant = cell.TextureVariant };
        }).ToArray();
    }

    public static bool HasWater(TerrainStyle style, int x, int y, Func<int, int, bool> bridge) =>
        style.Water || style.BridgeWater && (bridge(x + 1, y - 1) || bridge(x - 3, y - 1) ||
            bridge(x - 1, y + 1) || bridge(x - 1, y - 3));

    public static MapPackage Compile(MapDefinition map, MapBuildingPlacement[] buildings,
        MapResources resources, ScenarioInitialState? scenario, IReadOnlyDictionary<int, int>? regionCities = null)
    {
        var squares = Squares(map);
        var styles = resources.TerrainStyles.ToDictionary(s => s.Id);
        for (int i = 0; i < map.Cells.Length; i++)
            if (!styles.TryGetValue(map.Cells[i].Terrain, out var style) || !style.Supports(map.Cells[i]))
                throw new InvalidDataException($"素材不支持格 ({i % map.Width}, {i / map.Width}) 的地貌组合。");
        var size = MapDisplayMetrics.CanvasSize(map.Width, map.Height);
        var terrain = Terrain(map, squares, resources, styles);
        var bridges = map.Cells.Select((c, i) => (c, i)).Where(p => p.c.BridgeOrientation is not null).Select(p =>
        {
            var center = MapDisplayMetrics.Center(p.i % map.Width, p.i / map.Width, map.Height);
            return new MapSprite($"bridge-{p.c.BridgeOrientation}", center.X - 96, center.Y - 16);
        }).ToArray();
        bool Bridge(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height &&
            map.Cells[y * map.Width + x].BridgeOrientation is not null;
        var water = squares.Select((s, i) => (s, i)).Where(p =>
            HasWater(styles[p.s.Terrain], p.i % map.Width, p.i / map.Width, Bridge)).Select(p =>
        {
            int x = p.i % map.Width, y = p.i / map.Width;
            var center = MapDisplayMetrics.Center(x, y, map.Height);
            return new MapAnimatedSprite($"water-{(x & 1) + 2 * (y & 1)}", center.X - 32, center.Y - 16, 0, -1);
        }).ToArray();
        var edges = Edges(map.Width, map.Height, squares, size.Height);
        string season = (scenario?.Month ?? 1) switch
        {
            >= 1 and <= 3 => "spring", >= 4 and <= 6 => "summer",
            >= 7 and <= 9 => "autumn", >= 10 and <= 12 => "winter",
            _ => throw new InvalidDataException("Invalid map month.")
        };
        return new(MapPackage.CurrentSchemaVersion, map.SourceBuild, scenario?.ScenarioId ?? "shared-map", season,
            map.Width, map.Height, size.Width, size.Height, squares, resources.Seasons, resources.Assets,
            resources.Masks, terrain, bridges, buildings, resources.Compositions,
            scenario is null ? [] : Clouds(map, squares, buildings, resources, scenario), edges.Left, edges.Right, [],
            new(resources.Clips, water), Boundaries(map.Width, map.Height, squares, regionCities ?? scenario!.RegionCityIds))
        { Authoring = map, Units = scenario is null ? resources.Units : resources.Units.BindFormations(scenario.Information.Formations),
            BuildingTypes = map.BuildingTypes, MinimapColors = resources.MinimapColors };
    }

    private static TerrainStamp[] Terrain(MapDefinition map, MapSquare[] squares, MapResources resources,
        IReadOnlyDictionary<int, TerrainStyle> styles)
    {
        int[] atlas = squares.Select(s =>
        {
            var style = styles[s.Terrain];
            int edge = style.Transitions ? BitOperations.PopCount((uint)s.ElevationMask) switch
                { 1 or 3 => 4, 2 => s.ElevationMask is 5 or 9 ? 12 : 8, _ => 0 } : 0;
            return style.Priority * 16 + s.Elevation * 272 + edge + style.OrderVariants[s.TextureVariant];
        }).ToArray();
        int Key(int x, int y) => x < 0 || y < 0 || x >= map.Width || y >= map.Height ? -1 : atlas[y * map.Width + x];
        var groups = new SortedDictionary<long, LinkedList<TerrainStamp>>();
        long minimum = long.MaxValue, maximum = long.MinValue;
        void Add(int x, int y, int part, int flags)
        {
            var square = squares[y * map.Width + x];
            var style = styles[square.Terrain];
            int mask = square.ElevationMask, h = square.Elevation;
            int resource = style.Levels.Single(l => l.Elevation == h).Resources[mask * style.VariantCount + square.TextureVariant];
            if (resource < 0) throw new InvalidDataException($"素材不支持格 ({x}, {y}) 的高差形态。");
            var center = MapDisplayMetrics.Center(x, y, map.Height);
            int tx = center.X + resources.MaskPositions[(mask * 9 + part) * 2] - 64;
            int ty = center.Y + resources.MaskPositions[(mask * 9 + part) * 2 + 1] - resources.MaskHeightAdjustments[mask] - 10 * h - 54;
            int maskId = mask != 0 ? 10806 + 9 * mask + part : flags == 1 ? 10950 + part : flags == 2 ? 10959 + part : 10806 + part;
            var stamp = new TerrainStamp(resource, maskId, tx, ty, tx - 96, ty - 112, part == 0);
            long order = x + map.Width * (y + (long)atlas[y * map.Width + x] * map.Height);
            if (!groups.TryGetValue(order, out var list)) groups[order] = list = new();
            if (minimum == long.MaxValue || order >= (minimum + maximum) / 2) list.AddLast(stamp);
            else list.AddFirst(stamp);
            minimum = Math.Min(minimum, order); maximum = Math.Max(maximum, order);
        }
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var current = styles[squares[y * map.Width + x].Terrain];
            foreach (var direction in Directions)
            {
                int nx = x + direction.X, ny = y + direction.Y, next = Key(nx, ny);
                if (next <= Key(x, y) || direction.X != 0 && direction.Y != 0 &&
                    (next <= Key(nx, y) || next <= Key(x, ny))) continue;
                var neighbor = squares[ny * map.Width + nx];
                int flags = current.WaterEdge ? 1 : neighbor.Elevation == 1 && styles[neighbor.Terrain].ShoreEdge ? 2 : 0;
                Add(nx, ny, direction.Part, flags);
            }
            Add(x, y, 0, 0);
        }
        return groups.Values.SelectMany(g => g).ToArray();
    }

    public static MapSprite[] Boundaries(int width, int height, MapSquare[] squares, IReadOnlyDictionary<int, int> cities)
    {
        List<MapSprite> output = [];
        (int DX, int DY, int X, int Y, int Primary, int Secondary, int Orientation)[] directions =
            [(1, 0, 32, 16, 8, 2, 0), (0, 1, 0, 16, 4, 2, 3)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        foreach (var direction in directions)
        {
            int nx = x + direction.DX, ny = y + direction.DY;
            if (nx >= width || ny >= height) continue;
            var square = squares[y * width + x]; var next = squares[ny * width + nx];
            if (square.RegionId < 0 || next.RegionId < 0 || square.RegionId == next.RegionId) continue;
            bool city = cities.TryGetValue(square.RegionId, out int a) && cities.TryGetValue(next.RegionId, out int b) && a != b;
            bool primary = (square.ElevationMask & direction.Primary) != 0, secondary = (square.ElevationMask & direction.Secondary) != 0;
            int shape = primary == secondary ? 0 : primary ? 1 : 2;
            var center = MapDisplayMetrics.Center(x, y, height);
            output.Add(new($"boundary-{(city ? 10800 : 10794) + direction.Orientation + shape}",
                center.X - 32 + direction.X, center.Y - 16 + direction.Y + 10 * ((primary ? 0 : 1) - square.Elevation)));
        }
        return output.ToArray();
    }

    private static (int?[] Left, int?[] Right) Edges(int width, int height, MapSquare[] squares, int canvasHeight)
    {
        (int X, int Y) Point(int x, int y, int direction)
        {
            var center = MapDisplayMetrics.Center(x, y, height); var square = squares[y * width + x];
            var offset = direction switch { 1 => (32, 0), 4 => (0, 16), _ => (64, 16) };
            return (center.X - 32 + offset.Item1, center.Y - 16 + offset.Item2 + 10 -
                10 * (square.Elevation + ((square.ElevationMask & direction) != 0 ? 1 : 0)));
        }
        List<(int X, int Y)> left = [], right = [];
        for (int y = 0; y < height; y++) left.Add(Point(0, y, 1));
        left.Add(Point(0, height - 1, 4));
        for (int x = 1; x < width; x++) left.Add(Point(x, height - 1, 4));
        for (int x = 0; x < width; x++) right.Add(Point(x, 0, 1));
        right.Add(Point(width - 1, 0, 8));
        for (int y = 1; y < height; y++) right.Add(Point(width - 1, y, 8));
        int?[] Raster(List<(int X, int Y)> points)
        {
            int?[] rows = new int?[canvasHeight];
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                if (a.Y == b.Y) { if (a.Y >= 0 && a.Y < rows.Length) rows[a.Y] = (a.X + b.X) / 2; continue; }
                for (int y = Math.Max(0, Math.Min(a.Y, b.Y)); y <= Math.Min(rows.Length - 1, Math.Max(a.Y, b.Y)); y++)
                    rows[y] = a.X + (int)Math.Floor((double)(y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            return rows;
        }
        return (Raster(left), Raster(right));
    }

    private static MapSprite[] Clouds(MapDefinition map, MapSquare[] squares, MapBuildingPlacement[] buildings,
        MapResources resources, ScenarioInitialState scenario)
    {
        var assets = resources.Assets.ToDictionary(a => a.Id);
        var compositions = resources.Compositions.ToDictionary(c => c.Id);
        var types = map.BuildingTypes.ToDictionary(t => t.Id);
        var bounds = buildings.Select(b =>
        {
            var state = scenario.Settlements.Single(s => s.Id == b.Id);
            int composition = BuildingAppearance.CompositionId(b.FixedType, state.Appearance, state.Durability, types[b.FixedType].MaxDurability);
            var center = MapDisplayMetrics.Center(b.X, b.Y, map.Height);
            var sprites = BuildingAppearance.Sprites(compositions[composition], b.FixedType == 0,
                Math.Max(state.Commerce, state.Cultivation), center.X, center.Y - 10 * (squares[b.Y * map.Width + b.X].Elevation - 1));
            return (Left: sprites.Min(s => s.X), Top: sprites.Min(s => s.Y),
                Right: sprites.Max(s => s.X + assets[s.AssetId].Width), Bottom: sprites.Max(s => s.Y + assets[s.AssetId].Height));
        }).ToArray();
        List<MapSprite> output = [];
        int periods = (map.Width + map.Height) / 48 + 2;
        foreach (var placement in resources.CloudPlacements)
        for (int period = -1; period < periods; period++)
        {
            int side = placement.Side;
            var origin = side switch
            {
                0 => (32 * map.Height - 1536 * period, 768 * period),
                1 => (32 * map.Height + 1536 * period, 768 * period),
                2 => (1536 * period, 16 * map.Height + 768 * period),
                _ => (32 * (map.Width + map.Height) - 1536 * period, 16 * map.Width + 768 * period)
            };
            int worldY = origin.Item2 + placement.C;
            int start = Math.Max(0, (int)Math.Floor((worldY + (side < 2 ? 400 : 0)) / 16d));
            int end = Math.Min(start + 15, map.Width + map.Height - 2), count = Math.Max(1, end - start + 1), sum = 0;
            for (int diagonal = start; diagonal < end; diagonal++)
            {
                bool left = side is 0 or 2;
                int x = left ? Math.Max(0, diagonal - map.Height + 1) : Math.Min(map.Width - 1, diagonal);
                int y = diagonal - x;
                if (x < map.Width && y >= 0 && y < map.Height) sum += squares[y * map.Width + x].Elevation;
            }
            int px = 96 + origin.Item1 + placement.B, py = 112 + worldY - (int)Math.Floor(10d * (count + sum) / count);
            string id = $"cloud-{placement.A}"; var image = assets[id];
            if (bounds.Any(b => px < b.Right && b.Left < px + image.Width && py < b.Bottom && b.Top < py + image.Height))
                id = $"cloud-{placement.A - 11}";
            image = assets[id];
            var size = MapDisplayMetrics.CanvasSize(map.Width, map.Height);
            if (px < size.Width && py < size.Height && px + image.Width > 0 && py + image.Height > 0)
                output.Add(new(id, px, py));
        }
        return output.ToArray();
    }
}
