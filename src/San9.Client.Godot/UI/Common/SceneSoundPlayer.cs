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
    private readonly HashSet<AudioStreamPlayer> _overlapping = [];
    private float _transitionGain = 1;
    public void SetTransitionGain(float gain)
    {
        _transitionGain = gain;
        _player.VolumeDb = Mathf.LinearToDb(gain);
        foreach (var player in _overlapping) player.VolumeDb = _player.VolumeDb;
    }
    public override void _Ready() { _streams = new(ContentRoot, Sounds); _player = new() { MaxPolyphony = MaxPolyphony }; AddChild(_player); }
    public void Play(string role, bool enabled)
    {
        if (!enabled) return;
        var stream = _streams.Get(role, loop: false);
        if (_player.Stream != stream) _player.Stream = stream;
        _player.Play();
    }
    public void PlayAndFree(string role, bool enabled)
    {
        if (!enabled) { QueueFree(); return; }
        _player.Finished += QueueFree;
        Play(role, enabled: true);
    }
    public void PlayOverlapping(string role, bool enabled)
    {
        if (!enabled) return;
        var player = new AudioStreamPlayer
        { Stream = _streams.Get(role, loop: false), VolumeDb = Mathf.LinearToDb(_transitionGain) };
        _overlapping.Add(player); AddChild(player);
        player.Finished += () => { _overlapping.Remove(player); player.QueueFree(); };
        player.Play();
    }
    public void Stop()
    {
        _player.Stop(); _player.Stream = null;
        foreach (var player in _overlapping) { player.Stop(); player.Stream = null; player.QueueFree(); }
        _overlapping.Clear();
    }
    public override void _ExitTree()
    {
        Stop();
        _streams.Dispose();
    }
}
