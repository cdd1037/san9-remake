using Godot;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    public TurnDayPresentation? Day { get; }
    public SettlementView SampleSettlement(SettlementView value)
    {
        if (Day?.Battle is not { } battle) return value;
        var changes = battle.Values.Where(v => v.IsSettlement && v.TargetId == value.Id && v.Time <= Day.Elapsed).ToArray();
        return value with
        {
            Soldiers = (uint)Math.Clamp((long)value.Soldiers + changes.Sum(v => v.Soldiers), 0, uint.MaxValue),
            Morale = Math.Clamp(value.Morale + changes.Sum(v => v.Morale), 0, 100),
            Durability = Math.Clamp(value.Durability + changes.Sum(v => v.Durability), 0, value.MaxDurability)
        };
    }
    private readonly Dictionary<int, UnitVisualState> _unitStates;
    private Dictionary<int, MapUnitMotion> _motions = [];
    private MapAttackMotion[] _attacks = [];
    private Dictionary<int, UnitVisual> _sampledUnits = [];
    private Dictionary<int, UnitMotionPlayback> _unitPlayback = [];
    private readonly Func<double> _positionTime;
    private double _sampleTime = -1, _sampleVisualTime = -1;

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
        state = state with
        {
            ReceivesPositionUpdates = day is not null && milliseconds >= 0 &&
            milliseconds < MapMovementCadence.DayMilliseconds
        };
        if (motions.TryGetValue(id, out var motion))
        {
            var p = motion.Sample(milliseconds);
            var position = new WorldPosition((int)Math.Round(p.X), (int)Math.Round(p.Y));
            var runtime = snapshot.World.Units.Single(u => u.Id == id) with { Position = position };
            int formation = snapshot.FormationId(runtime)!.Value;
            bool moving = motion.HasDisplacement && state.ReceivesPositionUpdates;
            state = state with
            {
                CellX = (p.X - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                CellY = (p.Y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                Elevation = map.Squares[runtime.Cell.Y * map.Width + runtime.Cell.X].Elevation,
                Direction = motion.HasDisplacement ? p.Direction : state.Direction,
                FormationId = formation,
                VisionAngleDegrees = snapshot.Information.Formations.Single(f => f.Id == formation).Presentation?.VisionAngleDegrees ?? 0,
                HasCityInfluence = runtime.EffectiveOrderClass == UnitOrderClass.Military && snapshot.HasCityInfluence(runtime.OwnerId, position),
                IsMoving = moving,
                Action = moving ? UnitVisualAction.Moving : UnitVisualAction.Idle
            };
        }
        if (day is not null && milliseconds >= TurnDayPresentation.MovementMilliseconds)
        {
            state = state with { IsEngaged = day.After.IsUnitEngaged(id) };
            if (attacks.FirstOrDefault(a => !a.SourceIsSettlement && a.SourceId == id && day.IsAttackActive(a)) is { } attack)
                state = state with { Direction = attack.Direction, Action = attack.Action };
        }
        if (day?.Battle is { } battle)
        {
            var changes = battle.Values.Where(v => !v.IsSettlement && v.TargetId == id && v.Time * 1000 <= milliseconds).ToArray();
            uint soldiers = state.Soldiers, lastLiving = soldiers;
            foreach (var change in changes)
            {
                soldiers = (uint)Math.Clamp((long)soldiers + change.Soldiers, 0, uint.MaxValue);
                if (soldiers > 0) lastLiving = soldiers;
            }
            state = state with
            {
                Soldiers = soldiers,
                RetainedBodySoldiers = soldiers == 0 ? lastLiving : null,
                Morale = Math.Clamp(state.Morale + changes.Sum(v => v.Morale), 0, 100),
                Condition = changes.LastOrDefault(v => v.Condition is not null)?.Condition ?? state.Condition
            };
        }
        return state;
    }
    public UnitVisual Sample(UnitVisual unit)
    {
        AdvanceUnitPlayback();
        double time = MotionTime, visualTime = MotionVisualTime;
        if (_sampleTime != time || _sampleVisualTime != visualTime)
        { _sampleTime = time; _sampleVisualTime = visualTime; _sampledUnits.Clear(); }
        if (_sampledUnits.TryGetValue(unit.Id, out var cached)) return cached;
        var state = _stateSampler(unit.Id, time - _motionStart)!;
        return _sampledUnits[unit.Id] = UnitVisuals.Resolve(state, Map.Units, Clips, _unitPlayback[unit.Id], ElevationAt);
    }
    private void PrepareUnitPlayback(MapScene? previous)
    {
        if (previous is not null)
        {
            previous.AdvanceUnitPlayback();
            _unitPlayback = previous._unitPlayback.Where(p => _unitStates.ContainsKey(p.Key))
                .ToDictionary(p => p.Key, p => p.Value.Copy());
        }
        foreach (int id in _unitStates.Keys)
        {
            bool created = !_unitPlayback.TryGetValue(id, out var playback);
            if (created) _unitPlayback[id] = playback = new();
            if (created || previous is null || !ReferenceEquals(previous.Day, Day) || !ReferenceEquals(previous.State, State))
                playback!.Begin(_stateSampler(id, -MapMovementCadence.StepMilliseconds)!, Map.Units,
                    _motionStart, _visualStart, _motions.TryGetValue(id, out var motion) && motion.HasDisplacement);
        }
    }
    public long UnitAnimationRevision
    {
        get { AdvanceUnitPlayback(); return _unitPlayback.Values.Sum(p => p.Revision); }
    }
    public double PlaybackMilliseconds => MotionVisualTime;
    private double _playbackTime = -1, _playbackVisualTime = -1;
    private void AdvanceUnitPlayback()
    {
        double time = MotionTime, visualTime = MotionVisualTime;
        if (_playbackTime == time && _playbackVisualTime == visualTime) return;
        foreach (var (id, playback) in _unitPlayback)
            playback.Advance(time, visualTime, t => HistoryState(id, t) ?? _unitStates[id],
                Map.Units, Clips, EmissionTime);
        _playbackTime = time; _playbackVisualTime = visualTime;
    }
    public Rect2 MotionBounds(UnitVisual unit)
    {
        var bounds = new Rect2(unit.AnchorX, unit.AnchorY, 1, 1);
        if (_motions.TryGetValue(unit.Id, out var motion))
            foreach (var p in motion.Points) bounds = bounds.Expand(Project(p.X, p.Y));
        foreach (var segment in _history)
            if (segment.Paths.TryGetValue(unit.Id, out var previousPath))
                foreach (var p in previousPath.Segment(Math.Max(0, MotionTime - HistoryMilliseconds - segment.Start),
                    segment.End - segment.Start)) bounds = bounds.Expand(Project(p.X, p.Y));
        return bounds.Grow(256);
    }
    public IEnumerable<string> MotionAssets(IEnumerable<UnitVisual> units, Rect2 bounds)
    {
        foreach (var condition in Day?.Battle?.Values.Select(v => v.Condition).OfType<UnitCondition>().Distinct() ?? [])
            if (Map.Units.Conditions.TryGetValue(condition, out var effect))
                foreach (string frame in Clips[effect.ClipId].Frames) yield return frame;

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
        .Where(Day.IsAttackPresented)
        .SelectMany(a => MapProjectiles.Sample(a, Map.Units.Projectiles[a.Projectile], Day.CombatMilliseconds, Clips));
}
