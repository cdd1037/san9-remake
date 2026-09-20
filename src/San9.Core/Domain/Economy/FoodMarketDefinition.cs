using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record FoodMarketDefinition([property: JsonRequired] ImmutableArray<ImmutableArray<int>> MonthlyRates)
{
    public void Validate()
    {
        if (MonthlyRates.IsDefault || MonthlyRates.Length != 12 || MonthlyRates.Any(r => r.IsDefaultOrEmpty || r.Any(v => v <= 0)))
            throw new ArgumentException("Invalid monthly food exchange rates.");
    }
}
