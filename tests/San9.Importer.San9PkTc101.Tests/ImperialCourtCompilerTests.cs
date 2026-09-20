using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Simulation;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ImperialCourtCompilerTests
{
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(9, 0)]
    public void BiographyReferencesPreserveSuccessionOrderAndModeSpecificDeathCatalog(byte mode, int count)
    {
        var court = ImperialCourtContentCompiler.Compile(mode, [Person(565), Person(564), Person(563)], [0, 1, 2]);
        Assert.Equal(new[] { 2, 1, 0 }, court.SuccessionOrder); Assert.Equal(count, court.DeathEvents.Length);
        Assert.Equal(mode is 2 or 9 ? null : new San9.Core.Domain.EmperorTransferDefinition(11, 15, 5), court.TransferEvent);
        if (count == 0) return;
        Assert.Equal(new GameClock(189, 4, 1), court.DeathEvents[0].ScenarioStartsBefore);
        Assert.Equal(new GameClock(189, 4, 1), court.DeathEvents[0].EarliestDate);
        Assert.Equal(new GameClock(189, 8, 1), court.DeathEvents[1].ScenarioStartsBefore);
        Assert.Equal(new GameClock(197, 4, 1), court.DeathEvents[1].EarliestDate);
        Assert.All(court.DeathEvents, e => Assert.Equal(60, e.SurvivalChancePercent));
    }

    [Fact]
    public void MissingRetainedImperialPersonIsNotSilentlyDropped() => Assert.Throws<InvalidDataException>(() =>
        ImperialCourtContentCompiler.Compile(0, [Person(563), Person(564), Person(565)], [0, 1]));
}
