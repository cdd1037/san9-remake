using Godot;

namespace San9.Client.Godot;

/// <summary>A scene temporarily owns music and restores the previous track and playback position.</summary>
public partial class SceneMusicPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Tracks { get; init; } = null!;
    public bool Enabled { get; init; } = true;
    private AudioStreamPlayer _player = null!;
    private AudioStreamPlayer _map = null!;
    private string? _mapRole;
    private int _owners;
    private AudioStreams _streams = null!;
    public override void _Ready() { _streams = new(ContentRoot, Tracks); _player = new(); AddChild(_player); _map = new(); AddChild(_map); }
    public void SetMapTrack(string? role)
    {
        if (_mapRole == role) return;
        _mapRole = role; _map.Stop(); _map.Stream = null;
        if (!Enabled || role is null) return;
        _map.Stream = _streams.Get(role, loop: true); _map.Play(); _map.StreamPaused = _owners > 0;
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
            if (playing) _player.Play(position);
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
