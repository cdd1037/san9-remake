namespace San9.Core.Simulation;

public sealed record GiftReceiptPreview(uint MoneyBefore, uint MoneyAfter, uint FoodBefore, uint FoodAfter);

public sealed partial class SimulationEngine
{
    /// <summary>Receipt uses the same root account and saturation calculation as settlement, without answering the task.</summary>
    public GiftReceiptPreview? GetGiftReceiptPreview(Guid taskId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var world = PreparationWorld();
        if (world.Simulation.Tasks.SingleOrDefault(t => t.Id == taskId)?.Gift is not { } gift) return null;
        var treasury = ResourceAccounts.Root(world, gift.TargetForceId).Treasury;
        return new(treasury.Money, ResourceAccounts.Project(treasury.Money, gift.Offer.Kind == GiftKind.Money ? gift.Offer.Amount : 0),
            treasury.Food, ResourceAccounts.Project(treasury.Food, gift.Offer.Kind == GiftKind.Food ? gift.Offer.Amount : 0));
    }
}
