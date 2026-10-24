namespace San9.Client.Godot;

// Time, fact admission and dialogue completion only move forward within one day.
// Keep blocked due cues separate so an earlier blocked fact cannot hide a later ready cue.
internal sealed class PresentationCueTimeline<T>
{
    private readonly record struct Entry(T Cue, double Time, int Fact, string? After, string? Dialogue, int Order);
    private readonly Entry[] _future;
    private readonly List<Entry> _waiting = [];
    private readonly List<Entry> _ready = [];
    private IReadOnlyList<T> _snapshot = [];
    private int _next;
    private int _lastFact = int.MinValue;
    private int _completedCount = -1;

    public PresentationCueTimeline(IEnumerable<T> cues, Func<T, double> time, Func<T, int> fact,
        Func<T, string?> after, Func<T, string?>? dialogue = null)
    {
        _future = cues.Select((cue, order) => new Entry(cue, time(cue), fact(cue), after(cue), dialogue?.Invoke(cue), order))
            .OrderBy(e => e.Time).ToArray();
    }

    public IReadOnlyList<T> Read(double time, int lastFact, IReadOnlySet<string> completed)
    {
        bool changed = false;
        bool gatesChanged = lastFact != _lastFact || completed.Count != _completedCount;
        if (completed.Count != _completedCount)
            for (int i = _ready.Count - 1; i >= 0; i--)
                if (_ready[i].Dialogue is { } id && completed.Contains(id))
                { _ready.RemoveAt(i); changed = true; }
        if (gatesChanged)
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                var entry = _waiting[i];
                if (!Ready(entry, lastFact, completed)) continue;
                _waiting.RemoveAt(i);
                if (entry.Dialogue is { } id && completed.Contains(id)) continue;
                _ready.Add(entry); changed = true;
            }
        while (_next < _future.Length && _future[_next].Time <= time)
        {
            var entry = _future[_next++];
            if (entry.Dialogue is { } id && completed.Contains(id)) continue;
            if (Ready(entry, lastFact, completed)) { _ready.Add(entry); changed = true; }
            else _waiting.Add(entry);
        }
        _lastFact = lastFact; _completedCount = completed.Count;
        if (changed)
        {
            _ready.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            _snapshot = _ready.Select(e => e.Cue).ToArray();
        }
        return _snapshot;
    }

    private static bool Ready(Entry entry, int lastFact, IReadOnlySet<string> completed) =>
        entry.Fact <= lastFact && (entry.After is null || completed.Contains(entry.After));
}
