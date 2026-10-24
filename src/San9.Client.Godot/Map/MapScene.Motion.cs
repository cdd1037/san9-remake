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
    private readonly Dictionary<int, UnitVisualState> _unitStates;
    private Dictionary<int, MapUnitMotion> _motions = [];
    private MapAttackMotion[] _attacks = [];
    private Dictionary<int, UnitVisual> _sampledUnits = [];
    private readonly Dictionary<int, (long Revision, UnitVisual Visual)> _stationarySamples = [];
    private Dictionary<int, UnitMotionPlayback> _unitPlayback = [];
    private readonly Func<double> _positionTime;
    private double _sampleTime = -1, _sampleVisualTime = -1;

    private double ElevationAt(double groundX, double groundY) =>
        MapGeometry.SampleElevation(groundX, groundY, Map.Width, Map.Height, Map.Squares);

    private Vector2 Project(double x, double y)
    {
        double cx = (x - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double cy = (y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double gx = 32 * (cx + Map.Height - cy) + 96, gy = 16 * (cx + cy + 1) + 112;
        return new((float)gx, (float)(gy - 10 * (ElevationAt(gx, gy) - 1)));
    }
    // Captures immutable motion inputs, never the scene or its retained history.
    private Func<int, double, UnitVisualState?> StateSampler(MapScene? previous)
    {
        var states = _unitStates; var motions = _motions;
        var snapshot = State; var map = Map; var day = Day;
        var runtimeUnits = _runtimeUnits;
        // Keep the last presented movement vision while the next day is being prepared.
        // Stable strategy views and independent maps start without a movement overlay.
        HashSet<int> retainedVision = [];
        if (previous is not null && ReferenceEquals(previous.Map, map) &&
            (day is not null || snapshot.World.Simulation.Boundary is SimulationBoundary.Advancing or SimulationBoundary.AwaitingDecision))
            foreach (int id in states.Keys)
                if (previous._stateSampler(id, previous.LocalMotionTime)?.MovementVisionVisible == true)
                    retainedVision.Add(id);
        var visionAngles = snapshot.Information.Formations.ToDictionary(f => f.Id, f => f.Presentation?.VisionAngleDegrees ?? 0);
        var values = day?.Battle?.Values.Where(v => !v.IsSettlement).GroupBy(v => v.TargetId)
            .ToDictionary(g => g.Key, g => g.ToArray()) ?? [];
        var displacements = day?.Battle?.MapChanges.Where(c => c.Displacement is not null).GroupBy(c => c.Displacement!.UnitId)
            .ToDictionary(g => g.Key, g => g.ToArray()) ?? [];
        return (id, time) => SampleState(id, time, states, motions, snapshot, map, day, runtimeUnits, visionAngles, values, displacements, retainedVision);
    }
    private static UnitVisualState? SampleState(int id, double milliseconds,
        Dictionary<int, UnitVisualState> states, Dictionary<int, MapUnitMotion> motions,
        San9.Application.Presentation.StrategySnapshot snapshot,
        MapPackage map, TurnDayPresentation? day, IReadOnlyDictionary<int, UnitState> runtimeUnits,
        IReadOnlyDictionary<int, int> visionAngles, IReadOnlyDictionary<int, BattleValueCue[]> values,
        IReadOnlyDictionary<int, BattleMapChangeCue[]> displacements, IReadOnlySet<int> retainedVision)
    {
        if (!states.TryGetValue(id, out var state)) return null;
        state = state with { MovementVisionVisible = retainedVision.Contains(id) };
        // A prepared day can be visible while resources or UI are still blocking playback.
        if (day is { Elapsed: 0 }) return state;
        if (day is not null)
            state = state with { MovementVisionVisible = motions.TryGetValue(id, out var path) && path.HasDisplacement };
        state = state with
        {
            ReceivesPositionUpdates = day is not null && milliseconds >= 0 &&
            milliseconds < MapMovementCadence.DayMilliseconds
        };
        if (day is not null && milliseconds >= MapMovementCadence.DayMilliseconds)
            state = state with { IsEngaged = day.After.IsUnitEngaged(id) };
        (double X, double Y, int Direction)? point = null;
        if (motions.TryGetValue(id, out var motion))
        {
            var p = motion.Sample(milliseconds);
            point = (p.X, p.Y, motion.HasDisplacement ? p.Direction : state.Direction);
            bool moving = motion.HasDisplacement && state.ReceivesPositionUpdates;
            state = state with
            {
                IsMoving = moving,
                Action = moving ? UnitVisualAction.Moving : UnitVisualAction.Idle
            };
        }
        if (day is not null && displacements.TryGetValue(id, out var relocations))
        {
            var cue = relocations.LastOrDefault(c => c.Time * 1000 <= milliseconds &&
                day.IsFactReady(c.FactIndex) && day.IsDialogueComplete(c.AfterDialogueId));
            if (cue is not null)
            {
                point = cue.SampleDisplacement(milliseconds / 1000);
                bool moving = milliseconds < cue.End * 1000;
                state = state with
                {
                    IsEngaged = false, MovementVisionVisible = false, PositionMode = UnitPositionMode.Formation,
                    ReceivesPositionUpdates = false,
                    IsMoving = moving, Action = moving ? UnitVisualAction.Moving : UnitVisualAction.Idle
                };
            }
        }
        if (point is { } sample)
        {
            var position = new WorldPosition((int)Math.Round(sample.X), (int)Math.Round(sample.Y));
            var runtime = runtimeUnits[id] with { Position = position };
            int formation = snapshot.FormationId(runtime)!.Value;
            state = state with
            {
                CellX = (sample.X - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                CellY = (sample.Y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell,
                Elevation = map.Squares[runtime.Cell.Y * map.Width + runtime.Cell.X].Elevation,
                Direction = sample.Direction, FormationId = formation, VisionAngleDegrees = visionAngles[formation],
                HasCityInfluence = runtime.EffectiveOrderClass == UnitOrderClass.Military && snapshot.HasCityInfluence(runtime.OwnerId, position)
            };
        }
        if (day is not null && values.TryGetValue(id, out var changes))
        {
            uint soldiers = state.Soldiers, lastLiving = soldiers;
            int moraleDelta = 0;
            var condition = state.Condition;
            foreach (var change in changes)
            {
                if (change.Time * 1000 > milliseconds || !day.IsFactReady(change.FactIndex) || !day.IsDialogueComplete(change.AfterDialogueId)) continue;
                soldiers = (uint)Math.Clamp((long)soldiers + change.Soldiers, 0, uint.MaxValue);
                if (soldiers > 0) lastLiving = soldiers;
                moraleDelta += change.Morale;
                condition = change.Condition ?? condition;
            }
            state = state with
            {
                Soldiers = soldiers,
                RetainedBodySoldiers = soldiers == 0 ? lastLiving : null,
                Morale = Math.Clamp(state.Morale + moraleDelta, 0, 100),
                Condition = condition
            };
        }
        return state;
    }
    public UnitVisual Sample(UnitVisual unit)
    {
        AdvanceUnitPlayback();
        var playback = _unitPlayback[unit.Id];
        bool stationary = Day is null && _history.Length == 0 && playback.Trails.Count == 0 &&
            (playback.BodyAttack is not { } attack || playback.VisualTime >= attack.Start + attack.Duration);
        if (stationary && _stationarySamples.TryGetValue(unit.Id, out var previous) && previous.Revision == playback.Revision)
            return previous.Visual;
        double time = MotionTime, visualTime = MotionVisualTime;
        if (_sampleTime != time || _sampleVisualTime != visualTime)
        { _sampleTime = time; _sampleVisualTime = visualTime; _sampledUnits.Clear(); }
        if (_sampledUnits.TryGetValue(unit.Id, out var cached)) return cached;
        var state = _stateSampler(unit.Id, time - _motionStart)!;
        var visual = UnitVisuals.Resolve(state, Map.Units, Clips, playback, ElevationAt);
        if (stationary) _stationarySamples[unit.Id] = (playback.Revision, visual);
        return _sampledUnits[unit.Id] = visual;
    }
    private void PrepareUnitPlayback(MapScene? previous)
    {
        // Building-only display revisions retain the same unit inputs and playback clock.
        // A new world snapshot or unit set still receives independent member state.
        if (previous is not null && ReferenceEquals(previous.Day, Day) && ReferenceEquals(previous.State, State) &&
            ReferenceEquals(previous._unitStates, _unitStates))
        {
            _unitPlayback = previous._unitPlayback;
            return;
        }
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
    public long UnitAnimationRevision(UnitVisual unit, Rect2 viewport)
    {
        return IsUnitVisible(unit, viewport) ? _unitPlayback[unit.Id].Revision : -1;
    }
    public double PlaybackMilliseconds => MotionVisualTime;
    private double _playbackTime = -1, _playbackVisualTime = -1;
    private long _playbackPresentationRevision = -1;
    public void AdvanceUnitPlayback()
    {
        double time = MotionTime, visualTime = MotionVisualTime;
        long revision = Day?.PresentationRevision ?? 0;
        if (_playbackTime == time && _playbackVisualTime == visualTime && _playbackPresentationRevision == revision) return;
        if (_playbackPresentationRevision != revision)
        {
            _sampleTime = -1;
            if (Day is { } day)
                foreach (var attack in _attacks.Where(day.IsBodyPresented).GroupBy(a => a.SourceId).Select(g => g.MaxBy(a => a.FactIndex)!))
                    if (_unitPlayback.TryGetValue(attack.SourceId, out var playback))
                        playback.PlayAttack(new($"{day.Before.World.Revision}:{attack.FactIndex}", attack.Action, attack.Direction,
                            _visualStart + day.AttackVisualStart(attack), day.Plan!.BodyDuration(attack) * 1000, attack.BodyDelayMilliseconds));
        }
        foreach (var (id, playback) in _unitPlayback)
            playback.Advance(time, visualTime, Day is not { Elapsed: 0 }, t => HistoryState(id, t) ?? _unitStates[id],
                Map.Units, Clips, EmissionTime);
        _playbackTime = time; _playbackVisualTime = visualTime;
        _playbackPresentationRevision = revision;
    }
    public Rect2 MotionBounds(UnitVisual unit)
    {
        var bounds = new Rect2(unit.AnchorX, unit.AnchorY, 1, 1);
        if (_motions.TryGetValue(unit.Id, out var motion))
            foreach (var p in motion.Points) bounds = bounds.Expand(Project(p.X, p.Y));
        foreach (var change in Day?.Battle?.MapChanges ?? [])
            if (change.Displacement is { } displacement && displacement.UnitId == unit.Id)
                bounds = bounds.Expand(Project(displacement.From.X, displacement.From.Y)).Expand(Project(displacement.To.X, displacement.To.Y));
        foreach (var segment in _history)
            if (segment.Paths.TryGetValue(unit.Id, out var previousPath))
                foreach (var p in previousPath.Segment(Math.Max(0, MotionTime - HistoryMilliseconds - segment.Start),
                    segment.End - segment.Start)) bounds = bounds.Expand(Project(p.X, p.Y));
        return bounds.Grow(256);
    }
    private IEnumerable<MapAttackMotion> VisibleAttacks(Rect2 bounds)
    {
        var viewport = new MapPresentationBounds(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y);
        return _attacks.Where(a => a.Projectile != MapProjectileKind.None &&
            Enumerable.Range(0, MapProjectiles.ShotCount(a)).Any(i => MapProjectiles.IsVisible(a, i, viewport)));
    }
    public IEnumerable<MapProjectileSprite> Projectiles(Rect2 bounds) => Day is null ? [] : VisibleAttacks(bounds)
        .Where(Day.IsAttackPresented)
        .SelectMany(a => MapProjectiles.Sample(a, Map.Units.Projectiles[a.Projectile], Day.AttackMilliseconds(a), Clips, Day.PresentedShots(a)));
}
