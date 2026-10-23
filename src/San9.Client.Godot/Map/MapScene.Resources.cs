using Godot;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    private Dictionary<int, string[]> _unitMotionAssets = [];
    private IReadOnlyDictionary<int, UnitState> _runtimeUnits = null!;
    private string[] _sharedMotionAssets = [];

    private void PrepareMotionAssets(MapScene? previous)
    {
        // Coverage is independent of damage and playback time: include every layout and facing.
        bool sameInputs = previous is not null && ReferenceEquals(previous.Map, Map) && ReferenceEquals(previous.State, State);
        _runtimeUnits = sameInputs ? previous!._runtimeUnits : State.World.Units.ToDictionary(u => u.Id);
        _unitMotionAssets = sameInputs ? previous!._unitMotionAssets : [];
        _sharedMotionAssets = Day?.Battle?.Values.Select(v => v.Condition).OfType<UnitCondition>().Distinct()
            .Where(Map.Units.Conditions.ContainsKey).Select(c => Map.Units.Conditions[c].ClipId)
            .Concat(Map.Units.VisionSlices.Select(s => s.ClipId)).SelectMany(id => Clips[id].Frames).Distinct().ToArray()
            ?? Map.Units.VisionSlices.SelectMany(s => Clips[s.ClipId].Frames).Distinct().ToArray();
    }

    private IEnumerable<string> UnitMotionAssets(int id)
    {
        var runtime = _runtimeUnits[id];
        var state = _unitStates[id];
        var formations = new[] { runtime.FormationId, runtime.NavalFormationId, State.FormationId(runtime) }.OfType<int>().ToHashSet();
        var layouts = Map.Units.Formations.Where(f => formations.Contains(f.FormationId)).SelectMany(f => f.Layouts).ToArray();
        foreach (var layout in layouts)
            foreach (string frame in Clips[layout.Influence.ClipId].Frames) yield return frame;
        var bodyIds = layouts.SelectMany(l => l.Members).Select(m => m.BodyId).ToHashSet();
        var imperialIds = Map.Units.Bodies.Where(b => bodyIds.Contains(b.Id)).Select(b => b.ImperialBodyId).OfType<string>().ToHashSet();
        foreach (var body in Map.Units.Bodies.Where(b => bodyIds.Contains(b.Id) || imperialIds.Contains(b.Id)))
            foreach (string clip in body.DirectionClips.Concat(body.Attacks.SelectMany(a => a.Actions).SelectMany(a => a.DirectionClips))
                .Concat(body.Wake is {} wake ? new[] { wake.ClipId } : [])
                .Concat(body.Trail is {} trail ? new[] { trail.ClipId } : []).Concat(body.Engagement.Select(e => e.ClipId)))
                foreach (string frame in Clips[clip].Frames) yield return frame;
        int glyph = state.IsRulerCommander ? Map.Units.RulerFlagGlyph : state.Glyph;
        foreach (int direction in Enumerable.Range(0, 8))
            foreach (string idClip in FlagVisuals.Layers(direction, state.Color, glyph))
                if (Clips.TryGetValue(idClip, out var clip))
                    foreach (string frame in clip.Frames) yield return frame;
    }

    public IEnumerable<string> MotionAssets(IEnumerable<UnitVisual> units, Rect2 bounds)
    {
        foreach (string frame in _sharedMotionAssets) yield return frame;
        foreach (var unit in units)
        {
            if (!_unitMotionAssets.TryGetValue(unit.Id, out var frames))
                _unitMotionAssets[unit.Id] = frames = UnitMotionAssets(unit.Id).Distinct().ToArray();
            foreach (string frame in frames) yield return frame;
        }
        foreach (var attack in VisibleAttacks(bounds))
        {
            foreach (string frame in Map.Units.Projectiles[attack.Projectile].Frames) yield return frame;
            if (Map.Units.Projectiles[attack.Projectile].Impact is {} impact)
                foreach (string frame in Clips[impact.ClipId].Frames) yield return frame;
        }
    }
}
