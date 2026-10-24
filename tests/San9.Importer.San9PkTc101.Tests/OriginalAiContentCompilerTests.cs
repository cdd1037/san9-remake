using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OriginalAiContentCompilerTests
{
    [Theory]
    [InlineData(0, false, false, 0u)]
    [InlineData(1, false, false, 3000u)]
    [InlineData(2, true, false, 30000u)]
    [InlineData(3, true, false, 30000u)]
    [InlineData(4, true, false, 30000u)]
    [InlineData(5, true, false, 30000u)]
    [InlineData(6, false, false, 10000u)]
    [InlineData(7, false, false, 0u)]
    [InlineData(8, false, false, 15000u)]
    [InlineData(9, false, false, 15000u)]
    [InlineData(10, false, true, 0u)]
    [InlineData(11, false, true, 0u)]
    [InlineData(12, false, true, 0u)]
    [InlineData(13, false, true, 0u)]
    [InlineData(14, false, true, 0u)]
    [InlineData(15, false, true, 0u)]
    [InlineData(16, false, true, 0u)]
    public void MilitaryRolesReferenceFacilityDefinitionsInsteadOfGroupedCategories(
        int facilityId, bool fortified, bool obstacle, uint frontierReserve)
    {
        var policy = OriginalAiContentCompiler.Compile([], []).Military;

        Assert.Equal(fortified, policy.FortifiedFacilityIds.Contains(facilityId));
        Assert.Equal(obstacle, policy.RouteObstacleFacilityIds.Contains(facilityId));
        Assert.Equal(frontierReserve, policy.FrontierFacilityReserves.GetValueOrDefault(facilityId));
    }
}
