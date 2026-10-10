using Godot;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>A scene temporarily owns music and restores the previous track and playback position.</summary>
public partial class SceneMusicPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Tracks { get; init; } = null!;
    private AudioStreamPlayer _player = null!;
    private readonly Dictionary<string,AudioStreamWav> _streams = [];
    public override void _Ready() { _player = new(); AddChild(_player); }
    public IDisposable Begin(string role)
    {
        if (!_streams.TryGetValue(role,out var stream))
        {
            stream = AudioStreamWav.LoadFromFile(PackageFiles.Resolve(ContentRoot,Tracks[role]))
                ?? throw new InvalidDataException("Unable to load scene music: " + role);
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopEnd = (int)(stream.GetLength() * stream.MixRate);
            _streams.Add(role,stream);
        }
        var prior = _player.Stream;
        float position = _player.GetPlaybackPosition();
        bool playing = _player.Playing;
        _player.Stop(); _player.Stream = stream; _player.Play();
        return new Lease(() =>
        {
            if (!GodotObject.IsInstanceValid(_player)) return;
            _player.Stop(); _player.Stream = prior;
            if (playing) _player.Play(position);
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
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
