using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentFoodSupply(DomainEvent fact)
    {
        if (fact is not FoodExhaustedEvent exhausted) return false;
        if (!Player(exhausted.ForceId)) return true;
        clock = exhausted.Clock;
        Force("force", exhausted.ForceId);
        var corps = before.World.Corps.SingleOrDefault(c => c.Id == exhausted.CorpsId);
        location = corps?.LeaderId is int leader ? PersonLocation(leader) : null;
        Message("food-exhausted");
        Record("food-exhausted-record");
        // Paying the final full ration exhausts food without causing losses that day.
        var account = new ResourceAccount(ResourceOwnerKind.Corps, exhausted.CorpsId);
        if (events.Skip(index + 1).OfType<DailyAttritionEvent>().Any(e =>
            e.Clock == clock && e.Account == account && e.Starved && e.SoldierLoss > 0))
        {
            Message("starvation-loss");
            Record("starvation-loss-record", MapReportImportance.Detailed);
        }
        return true;
    }
}
