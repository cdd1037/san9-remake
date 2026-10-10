namespace San9.Client.Godot;

public enum MapSoundKind { Action, Construction, BattleAmbience }
public readonly record struct MapSoundSource(string Key, string Role, double X, double Y)
{
    public MapSoundKind Kind { get; init; }
    public string? Resource { get; init; }
    public long? Pulse { get; init; }
}
public readonly record struct MapSoundVoice(string Key, string Role, double X, double Y, double Gain)
{
    public long? Pulse { get; init; }
}

/// <summary>Pulse history belongs to a prepared day, not to the currently visible audio voices.</summary>
public sealed class MapSoundPulses
{
    private object? _day;
    private readonly Dictionary<string, long> _played = [];
    public void Begin(object? day)
    {
        if (ReferenceEquals(_day, day)) return;
        _day = day; _played.Clear();
    }
    public bool WasEmitted(MapSoundVoice voice) => voice.Pulse is long pulse && _played.TryGetValue(voice.Key, out long prior) && prior >= pulse;
    public void MarkEmitted(MapSoundVoice voice) { if (voice.Pulse is long pulse) _played[voice.Key] = pulse; }
}

/// <summary>Camera-local admission and bounded mixing, independent of gameplay and audio devices.</summary>
public static class MapAudioMix
{
    public static MapSoundVoice[] Select(IEnumerable<MapSoundSource> sources, double width, double height)
    {
        if (width <= 0 || height <= 0) return [];
        Dictionary<string, int> admitted = new(StringComparer.Ordinal);
        return sources.Where(s => s.X >= 0 && s.X < width && s.Y >= 0 && s.Y < height)
            .OrderBy(s => Math.Abs(s.X - width / 2)).ThenBy(s => s.Key, StringComparer.Ordinal)
            .Where(s =>
            {
                string resource = s.Resource ?? s.Role;
                int count = admitted.GetValueOrDefault(resource);
                if (count >= 3) return false;
                admitted[resource] = count + 1;
                return true;
            }).Take(24)
            .Select(s => new MapSoundVoice(s.Key, s.Role, s.X, s.Y,
                1 - Math.Abs(2 * s.X / width - 1) * 0.5) { Pulse = s.Pulse }).ToArray();
    }
}

/// <summary>One battle bed per audible scene; camera changes do not reroll or restart it.</summary>
public sealed class MapAudioPlayback(Func<int, int> choose)
{
    private string? _battleRole;
    public MapSoundVoice[] Update(IEnumerable<MapSoundSource> sources, double width, double height, string[] battleRoles)
    {
        var visible = sources.Where(s => s.X >= 0 && s.X < width && s.Y >= 0 && s.Y < height).ToArray();
        bool battle = visible.Any(s => s.Kind == MapSoundKind.BattleAmbience);
        if (!battle) _battleRole = null;
        var voices = MapAudioMix.Select(visible.Where(s => s.Kind != MapSoundKind.BattleAmbience &&
            (!battle || s.Kind != MapSoundKind.Construction)), width, height);
        if (!battle) return voices;
        _battleRole ??= battleRoles[choose(battleRoles.Length)];
        return [.. voices, new("battle", _battleRole, width / 2, height / 2, 1)];
    }
    public void Clear() => _battleRole = null;
}
