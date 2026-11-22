using San9.Content.Text;

namespace San9.Application.Presentation;

internal static class DispatchCounts
{
    public static CatalogColumn Column(TextCatalog texts) =>
        new("dispatched", CommandText.Resolve(texts, "label.dispatched"), 4, CatalogAlignment.Right);

    public static Dictionary<int, int> ByTarget(StrategySnapshot view, string taskKind)
    {
        var counts = new Dictionary<int, int>();
        foreach (var task in view.World.Simulation.Tasks)
        {
            if (task.ForceId != view.PlayerForceId || task.Kind != taskKind || task.Journey is not { } journey) continue;
            int? target = taskKind == "personnel-search" ? journey.DestinationPlaceId : journey.TargetOfficerId;
            if (target is int id) counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return counts;
    }
}
