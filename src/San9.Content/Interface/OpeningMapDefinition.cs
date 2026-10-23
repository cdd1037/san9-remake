using San9.Content.Images;

namespace San9.Content.Interface;

public sealed record OpeningMapRun(int X, int Y, int Length);
public sealed record OpeningMapCity(ScenarioPreviewPoint Center, OpeningMapRun[] Area);
public sealed record OpeningMapColor(string Marker, string? PreviewMarker, string Rgb);
public sealed record OpeningMapPreview(StartMenuSprite Background, StartMenuSprite Inset,
    double ScaleX, double ScaleY, double OffsetX, double OffsetY)
{
    public (double X, double Y) Project(ScenarioPreviewPoint center) => (center.X * ScaleX + OffsetX, center.Y * ScaleY + OffsetY);
}

/// <summary>One authored geography for every opening page. Page placement and scale are consumer concerns.</summary>
public sealed record OpeningMapDefinition(int Width, int Height, string Terrain, string Borders,
    ScenarioPreviewInset Inset, Dictionary<int, OpeningMapCity> Cities, Dictionary<string, OpeningMapColor> Colors,
    string NeutralColor)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> Assets => new[] { Terrain, Borders, Inset.Sprite.Asset }
        .Concat(Colors.Values.Select(c => c.Marker)).Concat(Colors.Values.Select(c => c.PreviewMarker).OfType<string>());

    public int? CityAt(int x, int y)
    {
        foreach (var (id, city) in Cities)
            if (city.Area.Any(r => r.Y == y && x >= r.X && x < r.X + r.Length)) return id;
        return null;
    }

    public void Validate(IReadOnlyDictionary<string, ImageAsset> assets)
    {
        if (Width <= 0 || Height <= 0 || Cities.Count == 0 || !Cities.ContainsKey(Inset.CityId) ||
            !Colors.ContainsKey(NeutralColor) || Assets.Any(a => !assets.ContainsKey(a)) ||
            Colors.Values.Any(c => c.Rgb.Length != 6 || !uint.TryParse(c.Rgb, System.Globalization.NumberStyles.HexNumber, null, out _) ||
                assets[c.Marker].Width % 4 != 0) ||
            Cities.Values.Any(c => c.Center.X < 0 || c.Center.X >= Width || c.Center.Y < 0 || c.Center.Y >= Height ||
                c.Area.Length == 0 || c.Area.Any(r => r.X < 0 || r.Y < 0 || r.Y >= Height || r.Length <= 0 || (long)r.X + r.Length > Width)))
            throw new InvalidDataException("Invalid opening map definition.");
        var occupied = new HashSet<int>();
        foreach (var city in Cities.Values)
        foreach (var run in city.Area)
        for (int x = run.X; x < run.X + run.Length; x++)
            if (!occupied.Add(run.Y * Width + x)) throw new InvalidDataException("Overlapping opening city areas.");
    }
}
