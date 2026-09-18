using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TacticStudyContentCompilerTests
{
    [Fact]
    public void ConvertsNineKnownCoursesWithoutInventingUnconfiguredCities()
    {
        var all = TacticStudyContentCompiler.Compile(Enumerable.Range(0, 50).ToHashSet());
        Assert.Equal(9, all.Length);
        Assert.Equal(new[] { 0, 3, 6, 9, 12, 15, 19, 23, 27 }, all.Select(c => c.TacticId).Order());
        var sparse = TacticStudyContentCompiler.Compile(new HashSet<int> { 3, 8, 49, 9000 });
        Assert.Equal(new[] { (3, 0), (8, 23) }, sparse.Select(c => (c.CityId, c.TacticId)));
        Assert.Empty(TacticStudyContentCompiler.Compile(new HashSet<int>()));
    }
}
