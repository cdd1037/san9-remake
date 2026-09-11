namespace San9.Core.Simulation;

/// <summary>
/// SplitMix64 with a project-owned, stable algorithm. Do not replace it with a platform RNG.
/// </summary>
public sealed class StableRandom
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public StableRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong State => _state;

    public static ulong DeriveSeed(ulong seed, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // Stable FNV-1a mixing over UTF-8; this is random stream separation, not integrity checking.
        foreach (byte value in System.Text.Encoding.UTF8.GetBytes(name))
            seed = unchecked((seed ^ value) * 1099511628211UL);
        return seed;
    }

    public ulong NextUInt64()
    {
        ulong value = _state += Gamma;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
