using San9.Content.Interface;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Movement audio belongs to the day's displacement, including the visual movement tail.</summary>
public static class MapUnitAudio
{
    public static string[] Select(MapBodySounds sounds, UnitVisualAction action, bool displaced, bool engaged, bool buildingTarget = false) => action switch
    {
        UnitVisualAction.Melee when buildingTarget && sounds.BuildingMelee.Length > 0 => sounds.BuildingMelee,
        UnitVisualAction.Melee => sounds.Melee,
        UnitVisualAction.Ranged => sounds.Ranged,
        UnitVisualAction.Moving or UnitVisualAction.Idle when displaced && !engaged => sounds.Movement,
        _ => []
    };
}

public enum MapSoundKind { Action, Movement, Construction, BattleAmbience }
public readonly record struct MapSoundSource(string Key, string Role, double X, double Y)
{
    public MapSoundKind Kind { get; init; }
    public string? Resource { get; init; }
    public string ResourceKey => Resource ?? Role;
    public long? Pulse { get; init; }
    public MapIntermittentSound? Intermittent { get; init; }
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
    public static MapSoundVoice[] Select(IEnumerable<MapSoundSource> sources, double width, double height,
        IReadOnlySet<string>? playing = null) => Admit(sources, width, height, playing).Select(s => Voice(s, width)).ToArray();

    internal static MapSoundVoice Voice(MapSoundSource source, double width, double movementGain = 1) =>
        new(source.Key, source.Role, source.X, source.Y,
            (1 - Math.Abs(2 * source.X / width - 1) * 0.5) * (source.Kind == MapSoundKind.Movement ? movementGain : 1))
            { Pulse = source.Pulse };

    internal static IEnumerable<MapSoundSource> Admit(IEnumerable<MapSoundSource> sources, double width, double height,
        IReadOnlySet<string>? playing)
    {
        if (width <= 0 || height <= 0) yield break;
        Dictionary<string, int> admitted = new(StringComparer.Ordinal);
        HashSet<(string Resource, int Pan)> movementPans = [];
        int total = 0;
        foreach (var source in sources.Where(s => s.X >= 0 && s.X < width && s.Y >= 0 && s.Y < height)
            .OrderBy(s => playing?.Contains(s.Key) == true ? 0 : 1)
            .ThenBy(s => Math.Abs(s.X - width / 2)).ThenBy(s => s.Key, StringComparer.Ordinal))
        {
            int count = admitted.GetValueOrDefault(source.ResourceKey);
            if (count >= 3) continue;
            if (source.Kind == MapSoundKind.Movement)
            {
                int pan = (int)(200 * source.X / width);
                if (!movementPans.Add((source.ResourceKey, pan)) && playing?.Contains(source.Key) != true) continue;
            }
            admitted[source.ResourceKey] = count + 1;
            yield return source;
            if (++total == 24) yield break;
        }
    }
}

/// <summary>One battle bed per audible scene; camera changes do not reroll or restart it.</summary>
public sealed class MapAudioPlayback(Func<int, int> choose, Func<string, string> resource)
{
    private string? _battleRole;
    private Dictionary<string, string> _playing = [];
    private double? _gainTime;
    private object? _day;
    private int _movementGain = 100;
    private readonly Dictionary<string, double> _nextIntermittent = [];
    public void BeginDay(object day)
    {
        if (ReferenceEquals(_day, day)) return;
        _day = day;
        // Each day has its own visual clock; admitted voices and their gains belong to the audible scene.
        _gainTime = null;
        _nextIntermittent.Clear();
    }
    public MapSoundVoice[] Update(IEnumerable<MapSoundSource> sources, double width, double height, string[] battleRoles,
        double milliseconds = 0, bool moving = true)
    {
        var visible = sources.Where(s => s.X >= 0 && s.X < width && s.Y >= 0 && s.Y < height).ToArray();
        _gainTime ??= milliseconds - 160;
        int steps = (int)Math.Max(0, Math.Floor((milliseconds - _gainTime.Value) / 160));
        _movementGain = Math.Clamp(_movementGain + (moving ? 8 : -8) * steps, 0, 100);
        _gainTime += steps * 160;
        bool battle = visible.Any(s => s.Kind == MapSoundKind.BattleAmbience);
        if (!battle) _battleRole = null;
        var retained = visible.Where(s => _playing.TryGetValue(s.Key, out string? playingResource) && playingResource == s.ResourceKey)
            .Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var intermittent = new List<MapSoundSource>();
        foreach (var source in visible.Where(s => s.Intermittent is not null))
        {
            var effect = source.Intermittent!;
            if (_nextIntermittent.TryGetValue(source.Key, out double next) && milliseconds < next) continue;
            _nextIntermittent[source.Key] = milliseconds + effect.IntervalMilliseconds;
            if (choose(effect.ChanceDenominator) != 0) continue;
            string role = effect.Sounds[choose(effect.Sounds.Length)];
            intermittent.Add(source with { Role = role, Resource = resource(role),
                Pulse = (long)milliseconds, Intermittent = null });
        }
        var admitted = MapAudioMix.Admit(visible.Where(s => s.Intermittent is null && s.Kind != MapSoundKind.BattleAmbience &&
            (!battle || s.Kind != MapSoundKind.Construction)).Concat(intermittent), width, height, retained).ToArray();
        _playing = admitted.ToDictionary(s => s.Key, s => s.ResourceKey, StringComparer.Ordinal);
        var voices = admitted.Select(s => MapAudioMix.Voice(s, width, _movementGain / 100.0)).ToArray();
        if (!battle) return voices;
        _battleRole ??= battleRoles[choose(battleRoles.Length)];
        return [.. voices, new("battle", _battleRole, width / 2, height / 2, 1)];
    }
    public void Clear() { _battleRole = null; _playing.Clear(); _gainTime = null; }
}

/// <summary>Observes the displayed building state before camera and sound admission; never replays old damage.</summary>
internal sealed class MapBuildingAudio
{
    private MapPackage? _map;
    private Dictionary<int, (int Type, int Appearance, int Composition)> _previous = [];

    public MapSoundSource[] Observe(MapScene scene, MapAudioDefinition audio)
    {
        bool sameMap = ReferenceEquals(_map, scene.Map);
        var current = new Dictionary<int, (int Type, int Appearance, int Composition)>();
        List<MapSoundSource> sounds = [];
        foreach (var building in scene.Buildings)
        {
            var settlement = scene.Settlements[building.Id];
            if (settlement.IsUnderConstruction) continue;
            var value = (building.FixedType, settlement.Appearance, building.CompositionId);
            current.Add(building.Id, value);
            if (!sameMap || !_previous.TryGetValue(building.Id, out var old) || old.Type != value.FixedType ||
                old.Appearance != value.Appearance || value.CompositionId >= old.Composition ||
                !audio.BuildingDamageSounds.TryGetValue(building.FixedType, out var role)) continue;
            var anchor = BuildingInfoGeometry.Anchor(building.X, building.Y, building.FixedType,
                scene.Map.Squares[building.Y * scene.Map.Width + building.X].Elevation, scene.Map.Height);
            sounds.Add(new($"building-damage:{building.Id}", role, anchor.X, anchor.Y));
        }
        _map = scene.Map;
        _previous = current;
        return sounds.ToArray();
    }
}
