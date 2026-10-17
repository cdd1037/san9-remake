namespace San9.Core.Domain;

/// <summary>A query-owned random sequence. Never reads or advances a world's gameplay streams.</summary>
public struct NavigationRandom(ulong seed)
{
    private ulong state = seed;
    public bool ReplaceEqual()
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        ulong value = state;
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return ((value ^ (value >> 31)) & 1) != 0;
    }
}
