using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class CityTacticContentCompilerTests
{
    [Fact]
    public void NewGameInitializesEveryCityAndPreservesScenarioTactics()
    {
        int[] screenshot = [9, 9, 10, 9, 25, 24, 9, 9, 24, 9, 9, 25, 25, 9, 23];
        for (int city = 0; city < screenshot.Length; city++)
            Assert.Equal(new[] { screenshot[city] }, CityTacticContentCompiler.Compile(city, []));
        var initialized = Enumerable.Range(0, 50).Select(i => CityTacticContentCompiler.Compile(i, []).Single()).ToArray();
        Assert.Equal(new[] { 23, 6, 4, 4, 6, 4, 1, 2 },
            new[] { 9, 10, 11, 23, 24, 25, 26, 30 }.Select(id => initialized.Count(t => t == id)));
        Assert.Equal(new[] { 9, 39, 40 }, CityTacticContentCompiler.Compile(6, [39, 40]));
        Assert.Equal(new[] { 9, 10, 39 }, CityTacticContentCompiler.Compile(0, [9, 10, 39]));
    }
}
