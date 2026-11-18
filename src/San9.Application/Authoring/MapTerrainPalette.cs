using San9.Content.Maps;

namespace San9.Application.Authoring;

/// <summary>Palette suggestions and valid brush choices for one terrain in the active texture theme.</summary>
public sealed class MapTerrainPalette
{
    private readonly int _terrain;
    private readonly bool _original;
    private readonly Dictionary<int, int[]> _variants;
    private readonly int[] _paintHeights;

    public MapTerrainPalette(TerrainStyle style, string theme)
    {
        _terrain = style.Id;
        _original = theme == "san9pk-tc-1.0.1.0-d20794ae" && style.Id is >= 0 and <= 16;
        _variants = style.Levels.ToDictionary(level => level.Elevation, level =>
            Enumerable.Range(0, style.VariantCount).Where(v =>
                style.Supports(new(style.Id, level.Elevation, v, null))).ToArray());
        Heights = _variants.Keys.Where(h => _variants[h].Length > 0).Order().ToArray();

        // TC101 also publishes fallback base images outside the native terrain height range.
        _paintHeights = Heights.Where(h => !_original || (_terrain switch
        {
            3 or 4 or 5 => h is >= 0 and <= 2,
            >= 6 and <= 12 or 15 or 16 => h == 1,
            _ => true
        })).ToArray();
        if (_paintHeights.Length == 0) _paintHeights = Heights;
    }

    public int[] Heights { get; }
    public bool CommonTerrain => !_original || _terrain is not (13 or 14);
    public bool CommonHeight(int h) => !_original || (_terrain is 0 or 1 or 2 ? h is >= 1 and <= 7 : h == 1);
    public bool CommonVariant(int v) => !_original || (_terrain switch
    {
        >= 6 and <= 8 => v is 0 or 1,
        >= 9 and <= 12 or 15 or 16 => v is 0 or 3,
        _ => v is >= 0 and <= 2
    });

    public int[] VariantsAt(int height) => _variants[height];

    public int ClosestHeight(int height) => _paintHeights.MinBy(h => Math.Abs((long)h - height));

    public int DefaultVariant(int height)
    {
        int preferred = _original ? _terrain switch
        {
            0 or 6 or 7 or 8 => 1,
            1 when height is >= 2 and <= 4 => 1,
            2 when height is >= 1 and <= 4 or 6 => 2,
            _ => 0
        } : 0;
        int[] valid = VariantsAt(height);
        return valid.Contains(preferred) ? preferred : valid[0];
    }

    public MapCellDefinition ReplaceTerrain(MapCellDefinition cell)
    {
        if (cell.Terrain == _terrain) return cell;
        int height = ClosestHeight(cell.Elevation);
        return cell with { Terrain = _terrain, Elevation = height, TextureVariant = DefaultVariant(height) };
    }
}
