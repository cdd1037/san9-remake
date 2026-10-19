using Godot;

namespace San9.Client.Godot;

/// <summary>App-owned music survives session UI replacement; scenes temporarily lease playback.</summary>
public partial class SceneMusicPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Tracks { get; init; } = null!;
    private readonly MusicPlaybackState _state = new();
    private readonly Dictionary<MusicPlaybackState.Track, AudioStreamPlayer> _players = [];
    private readonly HashSet<MusicPlaybackState.Track> _started = [];
    private AudioStreams _streams = null!;
    private float _mapGain = 1;
    public bool Enabled => _state.Enabled;

    public override void _Ready()
    {
        _streams = new(ContentRoot, Tracks);
        _state.Changed += Synchronize;
        Synchronize();
    }

    public void SetEnabled(bool enabled) => _state.SetEnabled(enabled);
    public void SetMapTrack(string? role) => _state.SetMapTrack(role is null ? null : Tracks[role]);
    public IDisposable Begin(string role, bool loop) => _state.Begin(Tracks[role], loop);
    public void SetMapTransitionGain(float gain)
    {
        if (_mapGain == gain) return;
        _mapGain = gain;
        UpdateGains();
    }

    private void Synchronize()
    {
        var live = _state.Tracks.ToHashSet();
        foreach (var track in _players.Keys.Where(t => !live.Contains(t)).ToArray())
        {
            var player = _players[track];
            player.Stop(); player.Stream = null;
            RemoveChild(player); player.QueueFree();
            _players.Remove(track); _started.Remove(track);
        }
        foreach (var track in live)
        {
            if (!_players.TryGetValue(track, out var player))
            {
                player = new() { Stream = _streams.GetFile(track.Resource, track.Loop) };
                AddChild(player); _players.Add(track, player);
            }
            bool audible = ReferenceEquals(track, _state.Audible);
            player.StreamPaused = !audible;
            // A finished one-shot remains finished until its owner closes it.
            if (audible && _started.Add(track)) player.Play();
        }
        UpdateGains();
    }

    private void UpdateGains()
    {
        foreach (var (track, player) in _players)
            player.VolumeDb = Mathf.LinearToDb(ReferenceEquals(track, _state.MapTrack) &&
                !(_state.HasScene && ReferenceEquals(track, _state.Audible)) ? _mapGain : 1);
    }

    public override void _ExitTree()
    {
        _state.Changed -= Synchronize;
        foreach (var player in _players.Values) { player.Stop(); player.Stream = null; }
        _players.Clear(); _started.Clear();
        _streams.Dispose();
    }
}
