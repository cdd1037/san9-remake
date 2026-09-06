using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Application.Presentation;

public readonly record struct RegionHoverInfo(string Region, string City)
{
    public static RegionHoverInfo Resolve(MapSquare square, StrategySnapshot scenario, TextCatalog texts)
    {
        if (square.HideRegionName || !scenario.RegionNameKeys.TryGetValue(square.RegionId, out var regionKey))
            return new("———", "——");
        string city = scenario.RegionCityIds.TryGetValue(square.RegionId, out int cityId) &&
            scenario.RegionNameKeys.TryGetValue(cityId, out var cityKey) ? texts.Resolve(cityKey) : "——";
        return new(texts.Resolve(regionKey), city);
    }
}
