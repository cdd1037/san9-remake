using Godot;

namespace San9.Client.Godot;

/// <summary>One-shot scene cues are owned by the scene rather than individual animation frames.</summary>
public partial class SceneSoundPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    private AudioStreamPlayer _player = null!;
    private AudioStreams _streams = null!;
    public override void _Ready() { _streams = new(ContentRoot, Sounds); _player = new(); AddChild(_player); }
    public void Play(string role, bool enabled)
    {
        if (!enabled) return;
        _player.Stream = _streams.Get(role, loop: false);
        _player.Play();
    }
    public void Stop() { _player.Stop(); _player.Stream = null; }
    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null;
        _streams.Dispose();
    }
}
