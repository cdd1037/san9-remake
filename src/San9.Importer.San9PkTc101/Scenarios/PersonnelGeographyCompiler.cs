using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class PersonnelGeographyCompiler
{
    public static ImmutableArray<CityConnection> CityConnections(IReadOnlyList<San9ScenarioSquareRecord> squares,
        int width, int height, IReadOnlyDictionary<int, int> regionCities, IReadOnlySet<int> portRegions)
    {
        if (width <= 0 || height <= 0 || (long)width * height != squares.Count)
            throw new ArgumentException("Invalid square grid.");
        var map = new San9.Content.Packages.MapDefinition(San9.Content.Packages.MapDefinition.CurrentSchemaVersion,
            San9PkTc101Detector.SourceBuildId, width, height, squares.Select(MapImporter.Cell).ToArray(), []);
        return San9.Content.Maps.MapGeographyCompiler.CityConnections(map, regionCities, portRegions,
            new HashSet<int> { 10, 15, 16 });
    }
}
