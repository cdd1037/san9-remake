using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace San9.Core.Simulation;

public sealed class WorldState
{
    private WorldState(GameClock clock, ulong randomState)
    {
        Clock = clock;
        RandomState = randomState;
    }

    public GameClock Clock { get; private set; }

    public ulong RandomState { get; private set; }

    public static WorldState Create(GameClock clock, ulong seed) => new(clock, seed);

    internal void CommitXun(GameClock clock, ulong randomState)
    {
        Clock = clock;
        RandomState = randomState;
    }

    internal WorldSnapshot CreateSnapshot()
    {
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{Clock.Year}|{Clock.Month}|{(int)Clock.Xun}|{RandomState}");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new WorldSnapshot(Clock, RandomState, hash);
    }
}

public sealed record WorldSnapshot(
    GameClock Clock,
    ulong RandomState,
    string StateHash);
