using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ForceFounderCompilerTests
{
    [Fact]
    public void BiographyOrderBecomesExplicitPriorityWithoutSourceScoreOrIdentityLimits()
    {
        int[] expected = [32, 5, 60, 38, 61, 62, 63, 64, 520, 66, 68, 74, 36, 3, 27];
        Assert.Equal(expected, Enumerable.Range(0, 700).Where(id => OfficerContentCompiler.FounderPriority(id) > 0)
            .OrderByDescending(OfficerContentCompiler.FounderPriority));
        Assert.Equal(Enumerable.Range(1, 15).Reverse(), expected.Select(OfficerContentCompiler.FounderPriority));
        Assert.Equal(0, OfficerContentCompiler.FounderPriority(9000));
    }
}
