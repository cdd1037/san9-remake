using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class ConstructionContentCompiler
{
    public static ImmutableArray<FacilityRule> Facilities(IReadOnlyList<San9ScenarioFixedBuildingRecord> buildings,
        IReadOnlyList<San9ScenarioFormationRecord> formations)
    {
        if (!buildings.Select(b => b.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioFixedBuildingArchive.RecordCount)))
            throw new InvalidDataException("Incomplete fixed building catalog.");
        if (!formations.Select(f => f.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioFormationArchive.RecordCount)))
            throw new InvalidDataException("Incomplete formation catalog.");
        var garrison = formations[12];
        var combat = new CombatProfile(garrison.AgainstUnits, garrison.AgainstSoldiers, 0, garrison.DefensePower, 50);
        return buildings.Select(b => new FacilityRule(b.Id, b.ConstructionCost, b.MaxDurability,
            new(b.FootprintWidth, b.FootprintHeight), b.RecoveryPerTurn, b.Id >= 6,
            b.Id < 10, b.Id < 10, b.Id == 0 ? 500000u : 250000u, b.Id == 16 ? 20 : null,
            b.Id >= 6 ? 19 : null, b.TacticId,
            Enumerable.Range(0, San9ScenarioFixedBuildingArchive.TacticCount).Where(b.HasTactic).ToImmutableArray())
            { Combat = combat, Attack = b.Id < 10 ? new(10, false) { ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 } : null, DestroyedAtZeroDurability = b.Id is >= 6 and <= 16,
                IsHarbor = b.Id == 1,
                Reprisal = b.Id == 16 ? new(6, 1000, 10, 10, 10, 5) : null,
                ConstructionGrowthTacticId = b.Id >= 6 ? b.Id == 16 ? 20 : 19 : null,
                DestructionGrowthTacticId = b.Id >= 10 ? b.Id == 16 ? 20 : 19 : null,
                InnateAttack = b.Id switch { 8 => new(false, 5), 9 => new(true, 0), _ => null },
                Tactics = b.Id switch
                {
                    0 => new(10, 3, true, [9, 10, 11, 23, 24, 25, 26, 27, 28, 29, 30]),
                    2 or 3 or 4 or 5 => new(10, 3, false, [11]),
                    7 or 9 => new(10, 3, false, [9]),
                    8 => new(10, 3, false, [10]),
                    _ => null
                } }).ToImmutableArray();
    }

    public static ImmutableArray<ConstructionSite> Sites(IReadOnlyList<San9ScenarioSquareRecord> squares,
        IReadOnlyList<San9ScenarioFixedBuildingRecord> buildings, IReadOnlySet<int> configuredRegions)
    {
        const int width = San9ScenarioSquareArchive.MapWidth, height = San9ScenarioSquareArchive.MapHeight;
        if (squares.Count != width * height || !squares.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, squares.Count)))
            throw new InvalidDataException("Incomplete construction square grid.");
        if (!buildings.Select(b => b.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioFixedBuildingArchive.RecordCount)))
            throw new InvalidDataException("Incomplete fixed building catalog.");
        var facilities = buildings.Where(b => b.Id >= 6).ToArray();
        var result = ImmutableArray.CreateBuilder<ConstructionSite>();
        foreach (var square in squares)
        {
            if (square.RegionCode is not int region || !configuredRegions.Contains(region) ||
                square.GridX == 0 || square.GridY == 0 || square.GridX == width - 1 || square.GridY == height - 1) continue;
            var eligible = facilities.Where(f => Eligible(square, f)).Select(f => f.Id).ToImmutableArray();
            if (!eligible.IsEmpty) result.Add(new(region, MapCoordinates.Center(new(square.GridX, square.GridY)), eligible));
        }
        return result.ToImmutable();

        bool Eligible(San9ScenarioSquareRecord center, San9ScenarioFixedBuildingRecord facility)
        {
            int mask = facility.Id switch { 10 or 12 or 14 => 1 << 13, 11 or 13 or 15 => 1 << 12, _ => 1 };
            // Source placement templates use whole terrain squares, independent of normalized runtime occupancy.
            var source = buildings[facility.Id];
            int left = center.GridX - source.FootprintWidth / 2, top = center.GridY - source.FootprintHeight / 2;
            if (left < 0 || top < 0 || left + source.FootprintWidth > width || top + source.FootprintHeight > height) return false;
            for (int y = top; y < top + source.FootprintHeight; y++)
            for (int x = left; x < left + source.FootprintWidth; x++)
            {
                var square = squares[y * width + x];
                if (square.RegionCode != center.RegionCode || (square.SquareFlags & mask) == 0) return false;
            }
            return true;
        }
    }
}
