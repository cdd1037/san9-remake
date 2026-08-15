using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class GameClockTests
{
    [Fact]
    public void Advance_MovesAcrossXunMonthAndYearBoundaries()
    {
        GameClock clock = new(190, 12, XunIndex.Middle);

        clock = clock.Advance();
        Assert.Equal(new GameClock(190, 12, XunIndex.Late), clock);

        clock = clock.Advance();
        Assert.Equal(new GameClock(191, 1, XunIndex.Early), clock);
    }
}
