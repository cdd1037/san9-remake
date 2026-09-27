using Godot;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    public TurnDayPresentation? Day { get; }
    private readonly Dictionary<int, UnitVisualState> _unitStates;
    private Dictionary<int, MapUnitMotion> _motions = [];
    private MapAttackMotion[] _attacks = [];
    private Dictionary<int, UnitVisual> _sampledUnits = [];
    private readonly Func<double> _positionTime;
    private double _sampleTime = -1, _sampleVisualTime = -1, _samplePositionTime = -1;

    private double ElevationAt(double groundX, double groundY) =>
        MapGeometry.SampleElevation(groundX, groundY, Map.Width, Map.Height, Map.Squares);

    private Vector2 Project(double x, double y)
    {
        double cx = (x - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double cy = (y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double gx = 32 * (cx + 200 - cy) + 96, gy = 16 * (cx + cy + 1) + 112;
        return new((float)gx, (float)(gy - 10 * (ElevationAt(gx, gy) - 1)));
    }
    // Captures immutable motion inputs, never the scene or its retained history.
    private Func<int, double, UnitVisualState?> StateSampler()
    {
        var states = _unitStates; var motions = _motions; var attacks = _attacks;
        var snapshot = State; var map = Map; var day = Day;
        return (id, time) => SampleState(id, time, states, motions, attacks, snapshot, map, day);
    }
    private static UnitVisualState? SampleState(int id, double milliseconds,
        Dictionary<int, UnitVisualState> states, Dictionary<int, MapUnitMotion> motions,
        MapAttackMotion[] attacks, San9.Application.Presentation.StrategySnapshot snapshot,
        MapPackage map, TurnDayPresentation? day)
    {
        if (!states.TryGetValue(id, out var state)) return null;
        if (motions.TryGetValue(id, out var motion))
        {
            var p = motion.Sample(milliseconds / TurnDayPresentation.MovementMilliseconds);
            var position = new WorldPosition((int)Math.Round(p.X), (int)Math.Round(p.Y));
            var runtime = snapshot.World.Units.Single(u => u.Id == id) with { Position = position };
            int formation = snapshot.FormationId(runtime)!.Value;
            state = state with
            {
                CellX = (p.X - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                CellY = (p.Y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                Elevation = map.Squares[runtime.Cell.Y * map.Width + runtime.Cell.X].Elevation,
                Direction = motion.HasDisplacement ? p.Direction : state.Direction, FormationId = formation,
                VisionAngleDegrees = snapshot.Information.Formations.Single(f => f.Id == formation).Presentation?.VisionAngleDegrees ?? 0,
                HasCityInfluence = runtime.EffectiveOrderClass == UnitOrderClass.Military && snapshot.HasCityInfluence(runtime.OwnerId, position),
                Action = motion.HasDisplacement && milliseconds < TurnDayPresentation.MovementMilliseconds ? UnitVisualAction.Moving : UnitVisualAction.Idle
            };
        }
        if (day is not null && milliseconds >= TurnDayPresentation.MovementMilliseconds)
        {
            state = state with { IsEngaged = day.After.IsUnitEngaged(id) };
            if (attacks.FirstOrDefault(a => !a.SourceIsSettlement && a.SourceId == id) is {} attack)
                state = state with { Direction = attack.Direction, Action = attack.Action };
        }
        return state;
    }
    public UnitVisual Sample(UnitVisual unit)
    {
        double time = MotionTime, visualTime = MotionVisualTime;
        double positionTime = _positionTime();
        if (_sampleTime != time || _sampleVisualTime != visualTime || _samplePositionTime != positionTime)
        { _sampleTime = time; _sampleVisualTime = visualTime; _samplePositionTime = positionTime; _sampledUnits.Clear(); }
        if (_sampledUnits.TryGetValue(unit.Id, out var cached)) return cached;
        var state = _stateSampler(unit.Id, time - _motionStart)!;
        UnitVisualState At(double t) => HistoryState(unit.Id, t) ?? state;
        return _sampledUnits[unit.Id] = UnitVisuals.Resolve(state, Map.Units, Clips, time,
            At, t => visualTime - EmissionTime(t), ElevationAt, positionTime);
    }
    public Rect2 MotionBounds(UnitVisual unit)
    {
        var bounds = new Rect2(unit.AnchorX, unit.AnchorY, 1, 1);
        if (_motions.TryGetValue(unit.Id, out var motion))
            foreach (var p in motion.Points) bounds = bounds.Expand(Project(p.X, p.Y));
        foreach (var segment in _history)
            if (segment.Paths.TryGetValue(unit.Id, out var previousPath))
                foreach (var p in previousPath.Segment(Math.Max(0, MotionTime - HistoryMilliseconds - segment.Start) / TurnDayPresentation.MovementMilliseconds,
                    (segment.End - segment.Start) / TurnDayPresentation.MovementMilliseconds)) bounds = bounds.Expand(Project(p.X, p.Y));
        return bounds.Grow(256);
    }
    public IEnumerable<string> MotionAssets(IEnumerable<UnitVisual> units, Rect2 bounds)
    {

        foreach (var slice in Map.Units.VisionSlices)
            foreach (string frame in Clips[slice.ClipId].Frames) yield return frame;
        foreach (var unit in units)
        {
            var runtime = State.World.Units.Single(u => u.Id == unit.Id);
            var formations = new[] { runtime.FormationId, runtime.NavalFormationId, State.FormationId(runtime) }.OfType<int>().ToHashSet();
            foreach (var layout in Map.Units.Formations.Where(f => formations.Contains(f.FormationId)).SelectMany(f => f.Layouts))
                foreach (string frame in Clips[layout.Influence.ClipId].Frames) yield return frame;
            var bodyIds = Map.Units.Formations.Where(f => formations.Contains(f.FormationId)).SelectMany(f => f.Layouts)
                .SelectMany(l => l.Members).Select(m => m.BodyId).ToHashSet();
            foreach (var body in Map.Units.Bodies.Where(b => bodyIds.Contains(b.Id) || Map.Units.Bodies.Any(original => bodyIds.Contains(original.Id) && original.ImperialBodyId == b.Id)))
                foreach (var clip in body.DirectionClips.Concat(body.MeleeActions.Concat(body.RangedActions).SelectMany(a => a.DirectionClips))
                    .Concat(body.Wake is {} wake ? new[] { wake.ClipId } : [])
                    .Concat(body.Trail is {} trail ? new[] { trail.ClipId } : []).Concat(body.Engagement.Select(e => e.ClipId)))
                    foreach (string frame in Clips[clip].Frames) yield return frame;
            // Facing can change anywhere along the day's path.
            foreach (var clip in Clips.Values.Where(c => c.Id.StartsWith("unit-flag-", StringComparison.Ordinal) &&
                (c.Id.EndsWith("pole-back", StringComparison.Ordinal) || c.Id.EndsWith("pole-front", StringComparison.Ordinal) ||
                 c.Id.EndsWith($"cloth-{_unitStates[unit.Id].Color}", StringComparison.Ordinal) ||
                 c.Id.EndsWith($"glyph-0-{(_unitStates[unit.Id].IsRulerCommander ? Map.Units.RulerFlagGlyph : _unitStates[unit.Id].Glyph)}", StringComparison.Ordinal) ||
                 c.Id.EndsWith($"glyph-1-{(_unitStates[unit.Id].IsRulerCommander ? Map.Units.RulerFlagGlyph : _unitStates[unit.Id].Glyph)}", StringComparison.Ordinal))))
                foreach (string frame in clip.Frames) yield return frame;
        }
        foreach (var attack in VisibleAttacks(bounds))
        {
            foreach (string frame in Map.Units.Projectiles[attack.Projectile].Frames) yield return frame;
            if (Map.Units.Projectiles[attack.Projectile].Impact is {} impact)
                foreach (string frame in Clips[impact.ClipId].Frames) yield return frame;
        }
    }
    private IEnumerable<MapAttackMotion> VisibleAttacks(Rect2 bounds) => _attacks.Where(a => a.Projectile != MapProjectileKind.None &&
        new Rect2((float)Math.Min(a.FromX, a.ToX), (float)Math.Min(a.FromY, a.ToY),
            (float)Math.Abs(a.ToX - a.FromX) + 1, (float)Math.Abs(a.ToY - a.FromY) + 1).Grow(256).Intersects(bounds));
    public IEnumerable<MapProjectileSprite> Projectiles(Rect2 bounds) => Day is null ? [] : VisibleAttacks(bounds)
        .SelectMany(a => MapProjectiles.Sample(a, Map.Units.Projectiles[a.Projectile], Day.CombatMilliseconds, Clips));
}
