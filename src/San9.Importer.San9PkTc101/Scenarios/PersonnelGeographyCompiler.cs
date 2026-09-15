using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class PersonnelGeographyCompiler
{
    public static TerritoryExits TerritoryExits(ushort sourceFlags) =>
        ((sourceFlags & 2) == 0 ? San9.Core.Domain.TerritoryExits.North : 0) |
        ((sourceFlags & 8) == 0 ? San9.Core.Domain.TerritoryExits.East : 0) |
        ((sourceFlags & 32) == 0 ? San9.Core.Domain.TerritoryExits.South : 0) |
        ((sourceFlags & 128) == 0 ? San9.Core.Domain.TerritoryExits.West : 0);

    public static ImmutableArray<CityConnection> CityConnections(IReadOnlyList<San9ScenarioSquareRecord> squares,
        int width, int height, IReadOnlyDictionary<int, int> regionCities, IReadOnlySet<int> portRegions)
    {
        if (width <= 0 || height <= 0 || (long)width * height != squares.Count)
            throw new ArgumentException("Invalid square grid.");
        bool Eligible(San9ScenarioSquareRecord square) => square.RegionCode is int region && regionCities.ContainsKey(region) &&
            (square.SquareFlags & 0x1FE) != 0x1FE &&
            (square.TerrainCode is not (10 or 15 or 16) || portRegions.Contains(region));
        HashSet<CityConnection> connections = [];
        for (int index = 0; index < squares.Count; index++)
        {
            var from = squares[index];
            if (!Eligible(from)) continue;
            int x = index % width;
            int y = index / width;
            void Add(int target, int directionMask)
            {
                if ((from.SquareFlags & directionMask) != 0 || !Eligible(squares[target])) return;
                int sourceCity = regionCities[from.RegionCode!.Value];
                int targetCity = regionCities[squares[target].RegionCode!.Value];
                if (sourceCity != targetCity) connections.Add(new(sourceCity, targetCity));
            }
            if (x + 1 < width) Add(index + 1, 8);
            if (y + 1 < height) Add(index + width, 32);
            if (x > 0) Add(index - 1, 128);
            if (y > 0) Add(index - width, 2);
        }
        return connections.OrderBy(c => c.FromCityId).ThenBy(c => c.ToCityId).ToImmutableArray();
    }
}
