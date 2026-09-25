using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OfficerStartingAgeTests
{
    [Theory]
    [InlineData(674, null)]
    [InlineData(675, 34)]
    [InlineData(676, 19)]
    [InlineData(677, 19)]
    [InlineData(678, 34)]
    [InlineData(679, 19)]
    [InlineData(680, 34)]
    [InlineData(681, 19)]
    [InlineData(682, 34)]
    [InlineData(683, 19)]
    [InlineData(684, 34)]
    [InlineData(685, 19)]
    [InlineData(686, null)]
    public void BiographyIdentityDeterminesStartingAge(int biography, int? expected) =>
        Assert.Equal(expected, OfficerContentCompiler.StartingAge(biography));
}
