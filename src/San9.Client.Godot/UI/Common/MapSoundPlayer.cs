using Godot;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Retains admitted loops and deduplicates animation pulses; owns every stream.</summary>
public partial class MapSoundPlayer : Node2D
{
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string, string> Sounds { get; init; } = null!;
    private readonly Dictionary<(string Path, bool Loop), AudioStream> _streams = [];
    private readonly Dictionary<string, (string Role, AudioStreamPlayer2D Player)> _voices = [];
    private readonly MapSoundPulses _pulses = new();

    public void UpdateVoices(IReadOnlyList<MapSoundVoice> voices, object? day)
    {
        _pulses.Begin(day);
        var retained = voices.Select(v => v.Key).ToHashSet();
        foreach (string key in _voices.Keys.Where(k => !retained.Contains(k)).ToArray())
        {
            var old = _voices[key].Player; old.Stop(); RemoveChild(old); old.QueueFree(); _voices.Remove(key);
        }
        foreach (var voice in voices)
        {
            if (!_voices.TryGetValue(voice.Key, out var active))
            {
                active = (voice.Role, new AudioStreamPlayer2D { MaxDistance = 1000000, Attenuation = 0 });
                AddChild(active.Player); _voices.Add(voice.Key, active);
            }
            var player = active.Player;
            player.Position = new((float)voice.X, (float)voice.Y);
            player.VolumeDb = (float)(20 * Math.Log10(voice.Gain));
            if (_pulses.WasEmitted(voice)) continue;
            if (voice.Pulse is null && active.Role == voice.Role && player.Playing) continue;
            bool loop = voice.Pulse is null;
            string path = Sounds[voice.Role];
            if (!_streams.TryGetValue((path, loop), out var stream))
            {
                stream = AudioStreams.Load(PackageFiles.Resolve(ContentRoot, path), loop);
                _streams.Add((path, loop), stream);
            }
            player.Stop(); player.Stream = stream; player.Play();
            _pulses.MarkEmitted(voice);
            _voices[voice.Key] = (voice.Role, player);
        }
    }
    public override void _ExitTree()
    {
        foreach (var voice in _voices.Values) { voice.Player.Stop(); voice.Player.Stream = null; }
        _voices.Clear();
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
