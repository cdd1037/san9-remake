using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record FoodExchangeRateChangedEvent(GameClock Clock, int Previous, int Current) : DomainEvent;
internal static class FoodMarket
{
    internal static void Refresh(WorldMutation world, FoodMarketDefinition? definition, List<DomainEvent> events)
    {
        if (definition is null) return;
        var pool = definition.MonthlyRates[world.Clock.Month - 1];
        int next = pool[(int)(world.NextRandom("food-market") % (ulong)pool.Length)];
        int previous = world.Simulation.FoodExchangeRate;
        world.SetSimulation(world.Simulation with { FoodExchangeRate = next });
        events.Add(new FoodExchangeRateChangedEvent(world.Clock, previous, next));
    }
}
