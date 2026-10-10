using Godot;

namespace San9.Client.Godot;

/// <summary>App-owned music survives session UI replacement; scenes temporarily lease playback.</summary>
public partial class SceneMusicPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Tracks { get; init; } = null!;
    public bool Enabled { get; private set; } = true;
    private AudioStreamPlayer _player = null!;
    private AudioStreamPlayer _map = null!;
    private string? _mapRole;
    private int _owners;
    private AudioStreams _streams = null!;
    public override void _Ready() { _streams = new(ContentRoot, Tracks); _player = new(); AddChild(_player); _map = new(); AddChild(_map); }
    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (!enabled) { _player.Stop(); _player.Stream = null; }
        RefreshMapTrack();
    }
    public void SetMapTrack(string? role)
    {
        if (_mapRole == role) return;
        _mapRole = role;
        RefreshMapTrack();
    }
    private void RefreshMapTrack()
    {
        if (!Enabled || _mapRole is null) { _map.Stop(); _map.Stream = null; return; }
        var stream = _streams.Get(_mapRole, loop: true);
        if (_map.Stream == stream && _map.Playing) return;
        _map.Stop(); _map.Stream = stream; _map.Play(); _map.StreamPaused = _owners > 0;
    }
    public IDisposable Begin(string role)
    {
        if (!Enabled) return new Lease(() => { });
        var stream = _streams.Get(role, loop: true);
        var prior = _player.Stream;
        float position = _player.GetPlaybackPosition();
        bool playing = _player.Playing;
        _owners++; _map.StreamPaused = true;
        _player.Stop(); _player.Stream = stream; _player.Play();
        return new Lease(() =>
        {
            if (!GodotObject.IsInstanceValid(_player)) return;
            _player.Stop(); _player.Stream = prior;
            if (Enabled && playing) _player.Play(position);
            _owners--; _map.StreamPaused = _owners > 0;
        });
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release,null)?.Invoke();
    }
    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null;
        _map.Stop(); _map.Stream = null;
        _streams.Dispose();
    }
}
