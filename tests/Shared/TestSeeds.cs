using San9.Core.Simulation;
using Xunit;

namespace San9.Tests;

// Known vectors for branch fixtures. RNG distribution tests keep their full input matrices.
internal static class TestSeeds
{
    private static readonly ulong[] PercentileStates =
    [
        29UL, 32UL, 52UL, 56UL, 277UL, 54UL, 235UL, 92UL, 24UL, 116UL,
        2UL, 64UL, 38UL, 11UL, 26UL, 179UL, 115UL, 81UL, 5UL, 119UL,
        178UL, 167UL, 8UL, 12UL, 100UL, 136UL, 45UL, 137UL, 9UL, 65UL,
        270UL, 44UL, 51UL, 25UL, 90UL, 165UL, 19UL, 200UL, 14UL, 50UL,
        107UL, 15UL, 53UL, 21UL, 20UL, 63UL, 22UL, 143UL, 237UL, 156UL,
        72UL, 352UL, 85UL, 3UL, 329UL, 60UL, 369UL, 77UL, 323UL, 132UL,
        43UL, 253UL, 62UL, 73UL, 309UL, 1UL, 10UL, 48UL, 95UL, 182UL,
        18UL, 36UL, 144UL, 98UL, 27UL, 123UL, 59UL, 111UL, 4UL, 17UL,
        82UL, 66UL, 305UL, 139UL, 246UL, 190UL, 152UL, 7UL, 108UL, 34UL,
        68UL, 288UL, 6UL, 83UL, 274UL, 13UL, 28UL, 226UL, 40UL, 217UL,
    ];

    internal static ulong StateForRoll(int roll)
    {
        ulong seed = PercentileStates[roll];
        Assert.Equal((ulong)roll, new StableRandom(seed).NextUInt64() % 100);
        return seed;
    }

    internal static ulong ForNamedRoll(string purpose, int roll)
    {
        ulong seed = (purpose, roll) switch
        {
            ("duel-trigger", 0) => 16UL,
            ("duel-trigger", 2) => 14UL,
            ("duel-trigger", 3) => 155UL,
            ("duel-trigger", 5) => 1UL,
            ("duel-trigger", 6) => 2UL,
            ("duel-trigger", 8) => 53UL,
            ("duel-trigger", 9) => 30UL,
            ("duel-trigger", 11) => 45UL,
            ("duel-trigger", 12) => 8UL,
            ("duel-trigger", 17) => 382UL,
            ("duel-trigger", 18) => 226UL,
            ("event-city-independence", 48) => 132UL,
            ("event-city-independence", 49) => 236UL,
            ("personnel-free-migration", 0) => 15UL,
            ("personnel-free-migration", 49) => 8UL,
            ("personnel-free-migration", 50) => 71UL,
            ("event-emperor-death", 59) => 19UL,
            ("event-emperor-death", 60) => 42UL,
            ("event-office-request", 0) => 175UL,
            ("event-office-request", 69) => 4UL,
            ("event-office-request", 70) => 44UL,
            _ => throw new ArgumentOutOfRangeException(nameof(roll))
        };
        Assert.Equal((ulong)roll, new StableRandom(StableRandom.DeriveSeed(seed, purpose)).NextUInt64() % 100);
        return seed;
    }
}
