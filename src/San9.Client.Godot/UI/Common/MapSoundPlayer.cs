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
    public override void _Ready() => _streams = new(ContentRoot, Sounds);

    public void UpdateVoices(IReadOnlyList<MapSoundVoice> voices, object? day)
    {
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
            player.VolumeDb = (float)(20 * Math.Log10(voice.Gain));
            if (_pulses.WasEmitted(voice)) continue;
            var stream = _streams.Get(voice.Role, loop: voice.Pulse is null);
            if (voice.Pulse is null && player.Stream == stream && player.Playing) continue;
            player.Stop(); player.Stream = stream; player.Play();
            _pulses.MarkEmitted(voice);
        }
    }
    public override void _ExitTree()
    {
        foreach (var player in _voices.Values) { player.Stop(); player.Stream = null; }
        _voices.Clear();
        _streams.Dispose();
    }
}
