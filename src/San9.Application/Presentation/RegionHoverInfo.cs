using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Application.Presentation;

public readonly record struct RegionHoverInfo(string Region, string City)
{
    public static RegionHoverInfo Resolve(MapPackage map, San9.Core.Domain.MapCell cell, StrategySnapshot scenario, TextCatalog texts)
    {
        if (cell.X < 0 || cell.X >= map.Width || cell.Y < 0 || cell.Y >= map.Height)
            throw new ArgumentOutOfRangeException(nameof(cell));
        return Resolve(map.Squares[cell.Y * map.Width + cell.X], scenario, texts);
    }

    public static RegionHoverInfo Resolve(MapSquare square, StrategySnapshot scenario, TextCatalog texts)
    {
        if (square.HideRegionName || !scenario.RegionNameKeys.TryGetValue(square.RegionId, out var regionKey))
            return new("———", "——");
        string city = scenario.RegionCityIds.TryGetValue(square.RegionId, out int cityId) &&
            scenario.Settlements.FirstOrDefault(s => s.Id == cityId) is { } settlement ? texts.Resolve(settlement.NameKey) : "——";
        return new(texts.Resolve(regionKey), city);
    }
}
