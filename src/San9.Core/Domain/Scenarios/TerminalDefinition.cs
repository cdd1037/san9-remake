using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Campaign victory geography and calendar limit. An empty city set disables the territorial alternative.</summary>
public sealed record TerminalDefinition(
    [property: System.Text.Json.Serialization.JsonRequired] ImmutableArray<int> CoreCityIds,
    [property: System.Text.Json.Serialization.JsonRequired] int? EndYear)
{
    public void Validate(IReadOnlySet<int> cityIds)
    {
        if (CoreCityIds.IsDefault || CoreCityIds.Distinct().Count() != CoreCityIds.Length ||
            CoreCityIds.Any(id => !cityIds.Contains(id)) || EndYear < 1)
            throw new ArgumentException("Invalid campaign victory references or year limit.");
    }
}
