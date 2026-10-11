using Godot;

namespace San9.Client.Godot;

/// <summary>One-shot scene cues are owned by the scene rather than individual animation frames.</summary>
public partial class SceneSoundPlayer : Node
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    public int MaxPolyphony { get; init; } = 1;
    private AudioStreamPlayer _player = null!;
    private AudioStreams _streams = null!;
    public void SetTransitionGain(float gain) => _player.VolumeDb = Mathf.LinearToDb(gain);
    public override void _Ready() { _streams = new(ContentRoot, Sounds); _player = new() { MaxPolyphony = MaxPolyphony }; AddChild(_player); }
    public void Play(string role, bool enabled)
    {
        if (!enabled) return;
        var stream = _streams.Get(role, loop: false);
        if (_player.Stream != stream) _player.Stream = stream;
        _player.Play();
    }
    public void Stop() { _player.Stop(); _player.Stream = null; }
    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null;
        _streams.Dispose();
    }
}
