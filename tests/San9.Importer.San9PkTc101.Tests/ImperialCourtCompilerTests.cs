using San9.Importer.San9PkTc101.Scenarios;
using San9.Core.Simulation;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ImperialCourtCompilerTests
{
    [Theory]
    [InlineData(190, 1, 1, 0u, false)]
    [InlineData(190, 1, 1, 1u, true)]
    [InlineData(220, 10, 23, 2u, false)]
    [InlineData(220, 10, 24, 0u, true)]
    [InlineData(220, 10, 25, 0u, true)]
    [InlineData(225, 1, 1, 129342u, true)]
    [InlineData(234, 1, 1, 129342u, true)]
    [InlineData(281, 1, 1, 128830u, true)]
    public void InitialCourtStateCombinesSerializedFlagWithScenarioStartDate(int year, int month, int day, uint flags, bool ended)
    {
        var court = ImperialCourtContentCompiler.Compile(1, [Person(563), Person(564), Person(565)], [0, 1, 2]);
        bool actual = ImperialCourtContentCompiler.IsInitiallyEnded([flags, 0, 0, 0], new(year, month, day));
        Assert.Equal(ended, actual);
        var people = Enumerable.Range(0, 3).Select(id => new San9.Core.Domain.OfficerState(id, null, 15, new(0, 0, 0, 0), false)
            { Status = San9.Core.Domain.OfficerStatus.Special });
        Assert.Equal(ended ? (int?)null : 0, court.Resolve(actual, people)?.Id);
    }

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
