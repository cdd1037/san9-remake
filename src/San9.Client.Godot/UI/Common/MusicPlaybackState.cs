namespace San9.Client.Godot;

/// <summary>Playback identities survive muting and temporary scenes; only live owners can resume.</summary>
public sealed class MusicPlaybackState
{
    public sealed class Track(string resource, bool loop)
    {
        public string Resource { get; } = resource;
        public bool Loop { get; } = loop;
    }

    private readonly List<(long Id, Track Track)> _scenes = [];
    private long _nextId;
    public event Action? Changed;
    public bool Enabled { get; private set; } = true;
    public Track? MapTrack { get; private set; }
    public bool HasScene => _scenes.Count > 0;
    private Track? Selected => HasScene ? _scenes[^1].Track : MapTrack;
    public Track? Audible => Enabled ? Selected : null;
    public IEnumerable<Track> Tracks => (MapTrack is { } map ? new[] { map } : [])
        .Concat(_scenes.Select(s => s.Track)).Distinct();

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        Changed?.Invoke();
    }

    public void SetMapTrack(string? resource)
    {
        if (MapTrack?.Resource == resource) return;
        MapTrack = resource is null ? null : Acquire(resource, loop: true);
        Changed?.Invoke();
    }

    public IDisposable Begin(string resource, bool loop)
    {
        long id = ++_nextId;
        _scenes.Add((id, Acquire(resource, loop)));
        Changed?.Invoke();
        return new Lease(() =>
        {
            _scenes.RemoveAll(s => s.Id == id);
            Changed?.Invoke();
        });
    }

    private Track Acquire(string resource, bool loop) => Selected is { } current &&
        current.Resource == resource && current.Loop == loop ? current : new(resource, loop);

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
