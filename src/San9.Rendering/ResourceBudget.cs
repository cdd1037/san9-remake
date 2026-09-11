namespace San9.Rendering;

// Engine-independent ownership and byte-based eviction, shared by file textures and generated terrain.
public sealed class ResourceBudget<TKey, TValue>(long budget, Action<TValue> release) : IDisposable where TKey : notnull
{
    private sealed class Entry(TValue value, long bytes, long use)
    {
        public TValue Value = value;
        public long Bytes = bytes, Use = use;
        public int Pins;
    }
    private readonly Dictionary<TKey, Entry> _entries = [];
    private long _clock;
    public long Budget { get; } = budget;
    public long Bytes => _entries.Values.Sum(e => e.Bytes);
    public long PinnedBytes => _entries.Values.Where(e => e.Pins > 0).Sum(e => e.Bytes);
    public int Count => _entries.Count;
    public long Evictions { get; private set; }
    public IEnumerable<TKey> Keys => _entries.Keys;
    public bool Contains(TKey key) => _entries.ContainsKey(key);
    public TValue Get(TKey key) { var e = _entries[key]; e.Use = ++_clock; return e.Value; }
    public void Add(TKey key, TValue value, long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        _entries.Add(key, new(value, bytes, ++_clock));
    }
    public IDisposable Pin(TKey key)
    {
        var entry = _entries[key]; entry.Pins++; entry.Use = ++_clock;
        return new PinLease(() => { entry.Pins--; entry.Use = ++_clock; });
    }
    public void Trim()
    {
        long bytes = Bytes;
        foreach (var pair in _entries.Where(p => p.Value.Pins == 0).OrderBy(p => p.Value.Use).ToArray())
        {
            if (bytes <= Budget) break;
            bytes -= pair.Value.Bytes; _entries.Remove(pair.Key); release(pair.Value.Value); Evictions++;
        }
    }
    public void Dispose() { foreach (var e in _entries.Values) release(e.Value); _entries.Clear(); }
    private sealed class PinLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
