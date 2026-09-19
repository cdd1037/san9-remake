using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ConsolidationLeadershipCompilerTests
{
    [Fact]
    public void OnlyTheElevenBiographyIdentitiesReceiveLeadershipPreference()
    {
        Assert.Equal(new[] { 3, 5, 36, 59, 60, 61, 62, 63, 64, 68, 520 },
            Enumerable.Range(0, 700).Where(OfficerContentCompiler.ConsolidationLeadership));
        Assert.False(OfficerContentCompiler.ConsolidationLeadership(9000));
    }
}
