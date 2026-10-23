namespace San9.Client.Godot;

/// <summary>Playback identities survive muting and temporary scenes; only live owners can resume.</summary>
public sealed class MusicPlaybackState
{
    public sealed class Track(string resource, bool loop)
    {
        public string Resource { get; } = resource;
        public bool Loop { get; } = loop;
    }

    private readonly List<Lease> _scenes = [];
    public event Action? Changed;
    public bool Enabled { get; private set; } = true;
    public Track? MapTrack { get; private set; }
    public bool HasScene => _scenes.Count > 0;
    private Track? Selected => HasScene ? _scenes[^1].Track : MapTrack;
    public Track? Audible => Enabled ? Selected : null;
    public float SceneGain => HasScene ? _scenes[^1].Gain : 1;
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

    public Lease Begin(string resource, bool loop)
    {
        var lease = new Lease(this, Acquire(resource, loop));
        _scenes.Add(lease);
        Changed?.Invoke();
        return lease;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var lease in _scenes.ToArray()) lease.Advance(seconds);
    }

    private void Release(Lease lease)
    {
        if (_scenes.Remove(lease)) Changed?.Invoke();
    }

    private Track Acquire(string resource, bool loop) => Selected is { } current &&
        current.Resource == resource && current.Loop == loop ? current : new(resource, loop);

    /// <summary>A fade retains playback ownership until silence; disposal cancels its completion.</summary>
    public sealed class Lease : IDisposable
    {
        private MusicPlaybackState? _owner;
        private double _fadeSeconds, _elapsed;
        private Action? _completed;
        internal Track Track { get; }
        internal float Gain { get; private set; } = 1;

        internal Lease(MusicPlaybackState owner, Track track) { _owner = owner; Track = track; }

        public void FadeOut(double seconds, Action completed)
        {
            if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (_owner is null || _fadeSeconds > 0) return;
            _fadeSeconds = seconds; _completed = completed;
        }

        internal void Advance(double seconds)
        {
            if (_owner is null || _fadeSeconds == 0) return;
            _elapsed = Math.Min(_elapsed + seconds, _fadeSeconds);
            Gain = (float)(1 - _elapsed / _fadeSeconds);
            if (_elapsed < _fadeSeconds) { _owner.Changed?.Invoke(); return; }
            var completed = _completed;
            Dispose();
            completed?.Invoke();
        }

        public void Dispose()
        {
            _completed = null;
            Interlocked.Exchange(ref _owner, null)?.Release(this);
        }
    }
}
