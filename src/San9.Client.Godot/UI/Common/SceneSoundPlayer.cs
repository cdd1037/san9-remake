using Godot;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>One-shot scene cues are owned by the scene rather than individual animation frames.</summary>
public partial class SceneSoundPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    private AudioStreamPlayer _player = null!;
    private readonly Dictionary<string,AudioStream> _streams = [];
    public override void _Ready() { _player = new(); AddChild(_player); }
    public void Play(string role, bool enabled)
    {
        if (!enabled) return;
        if (!_streams.TryGetValue(role,out var stream))
        {
            stream = AudioStreams.Load(PackageFiles.Resolve(ContentRoot,Sounds[role]), loop: false);
            _streams.Add(role,stream);
        }
        _player.Stream = stream;
        _player.Play();
    }
    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null;
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
