using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Authored references used by ending rules. Source-format city and item categories do not enter the resolver.</summary>
public sealed record EndingDefinition(
    ImmutableArray<int> ExpansionPublicOrderCityIds,
    ImmutableArray<int> ExpansionGatewayCityIds,
    ImmutableArray<int> ExpansionCommerceCityIds,
    ImmutableArray<int> DomesticDurabilityCityIds,
    ImmutableArray<int> DomesticGatewayCityIds,
    ImmutableArray<int> DomesticPublicOrderCityIds,
    ImmutableArray<int> CulturalTreasureIds)
{
    public void Validate(IReadOnlySet<int> cityIds, IReadOnlySet<int> treasureIds)
    {
        foreach (var group in new[] { ExpansionPublicOrderCityIds, ExpansionGatewayCityIds, ExpansionCommerceCityIds,
            DomesticDurabilityCityIds, DomesticGatewayCityIds, DomesticPublicOrderCityIds })
            if (group.IsDefaultOrEmpty || group.Distinct().Count() != group.Length || group.Any(id => !cityIds.Contains(id)))
                throw new ArgumentException("Ending geography must reference distinct valid cities in each group.");
        if (CulturalTreasureIds.IsDefault || CulturalTreasureIds.Distinct().Count() != CulturalTreasureIds.Length ||
            CulturalTreasureIds.Any(id => !treasureIds.Contains(id)))
            throw new ArgumentException("Ending treasure references must be distinct valid definitions.");
    }
}
