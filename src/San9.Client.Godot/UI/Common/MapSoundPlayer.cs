using Godot;

namespace San9.Client.Godot;

/// <summary>Retains admitted loops and deduplicates animation pulses; owns every stream.</summary>
public partial class MapSoundPlayer : Node2D
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string, string> Sounds { get; init; } = null!;
    private AudioStreams _streams = null!;
    private readonly Dictionary<string, AudioStreamPlayer2D> _voices = [];
    private readonly MapSoundPulses _pulses = new();
    private readonly Dictionary<AudioStreamPlayer2D, double> _oneShots = [];
    public float TransitionGain { get; set; } = 1;
    public override void _Ready() => _streams = new(ContentRoot, Sounds);

    public void PlayOnce(string role, Vector2 point, bool enabled, double gain = 1)
    {
        if (!enabled) return;
        var player = new AudioStreamPlayer2D { Position = point, MaxDistance = 1000000, Attenuation = 0,
            Stream = _streams.Get(role, loop: false), VolumeDb = Mathf.LinearToDb((float)gain * TransitionGain) };
        _oneShots.Add(player, gain); AddChild(player);
        player.Finished += () => { _oneShots.Remove(player); player.QueueFree(); };
        player.Play();
    }

    public void UpdateVoices(IReadOnlyList<MapSoundVoice> voices, object? day)
    {
        foreach (var (player, gain) in _oneShots) player.VolumeDb = Mathf.LinearToDb((float)gain * TransitionGain);
        _pulses.Begin(day);
        var retained = voices.Select(v => v.Key).ToHashSet();
        foreach (string key in _voices.Keys.Where(k => !retained.Contains(k)).ToArray())
        {
            var old = _voices[key]; old.Stop(); RemoveChild(old); old.QueueFree(); _voices.Remove(key);
        }
        foreach (var voice in voices)
        {
            if (!_voices.TryGetValue(voice.Key, out var player))
            {
                player = new AudioStreamPlayer2D { MaxDistance = 1000000, Attenuation = 0 };
                AddChild(player); _voices.Add(voice.Key, player);
            }
            player.Position = new((float)voice.X, (float)voice.Y);
            player.VolumeDb = Mathf.LinearToDb((float)voice.Gain * TransitionGain);
            if (_pulses.WasEmitted(voice)) continue;
            var stream = _streams.Get(voice.Role, loop: voice.Pulse is null);
            if (voice.Pulse is null && player.Stream == stream && player.Playing) continue;
            player.Stop(); player.Stream = stream; player.Play();
            _pulses.MarkEmitted(voice);
        }
    }
    public void StopOneShots()
    {
        foreach (var player in _oneShots.Keys) { player.Stop(); player.Stream = null; player.QueueFree(); }
        _oneShots.Clear();
    }
    public override void _ExitTree()
    {
        foreach (var player in _voices.Values) { player.Stop(); player.Stream = null; }
        _voices.Clear();
        foreach (var player in _oneShots.Keys) { player.Stop(); player.Stream = null; }
        _oneShots.Clear();
        _streams.Dispose();
    }
}
