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

    private int ElevationAt(double groundX, double groundY) =>
        MapGeometry.TrySquare(groundX, groundY, out int x, out int y) ? Map.Squares[y * Map.Width + x].Elevation : 1;

    private Vector2 Project(double x, double y)
    {
        double cx = (x - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double cy = (y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        int cellX = Math.Clamp((int)x / MapCoordinates.UnitsPerCell, 0, Map.Width - 1);
        int cellY = Math.Clamp((int)y / MapCoordinates.UnitsPerCell, 0, Map.Height - 1);
        return new((float)(32 * (cx + 200 - cy) + 96),
            (float)(16 * (cx + cy + 1) + 112 - 10 * (Map.Squares[cellY * Map.Width + cellX].Elevation - 1)));
    }
    private WorldPosition? Position(int id, bool settlement)
    {
        if (settlement) return Settlements.TryGetValue(id, out var s) ? MapCoordinates.Center(new(s.X, s.Y)) : null;
        return _motions.TryGetValue(id, out var motion) ? motion.Points.LastOrDefault() : State.World.Units.FirstOrDefault(u => u.Id == id)?.Position;
    }
    private void PrepareDay()
    {
        if (Day is null) return;
        _motions = Day.Events.OfType<UnitMovedEvent>().Where(e => e.From != e.To).GroupBy(e => e.UnitId)
            .ToDictionary(g => g.Key, g => new MapUnitMotion(g));
        List<MapAttackMotion> attacks = [];
        foreach (var hit in Day.Events.OfType<BattleDamageEvent>().Where(e => e.SoldierLoss > 0 || e.DurabilityLoss > 0 || e.MoraleLoss > 0))
        {
            bool targetSettlement = hit.SettlementId is not null;
            int targetId = hit.TargetUnitId ?? hit.SettlementId!.Value;
            if (Position(hit.AttackerId, hit.AttackerIsSettlement) is not {} from || Position(targetId, targetSettlement) is not {} to) continue;
            var source = Project(from.X, from.Y); var target = Project(to.X, to.Y);
            var kind = MapProjectileKind.None;
            MapMotionPoint[] path = [];
            MapMotionPoint[] origins = [];
            var action = UnitVisualAction.Melee;
            if (hit.AttackerIsSettlement)
            {
                var route = State.FacilityRockfallPath(hit.AttackerId, to);
                kind = route.Count > 0 ? MapProjectileKind.Rockfall : MapProjectileKind.Arrow;
                action = UnitVisualAction.Ranged;
                source.Y -= 36;
                if (kind == MapProjectileKind.Arrow && Buildings.FirstOrDefault(b => b.Id == hit.AttackerId) is {} building)
                {
                    var composition = Map.Compositions.Single(c => c.Id == building.CompositionId);
                    if (composition.ArrowOrigins.Length > 0)
                    {
                        var basePoint = Project(from.X, from.Y);
                        origins = composition.ArrowOrigins[UnitVisuals.Direction(from, to)].Select(p => new MapMotionPoint(basePoint.X + p.X, basePoint.Y + p.Y)).ToArray();
                    }
                }
                if (route.Count > 0) path = new[] { new MapMotionPoint(source.X, source.Y) }.Concat(route.Select(c =>
                {
                    var p = MapCoordinates.Center(c); var pixel = Project(p.X, p.Y);
                    return new MapMotionPoint(pixel.X, pixel.Y);
                })).Append(new(target.X, target.Y)).ToArray();
            }
            else if (_unitStates.TryGetValue(hit.AttackerId, out var unit))
            {
                var runtime = State.World.Units.Single(u => u.Id == unit.Id) with { Position = from };
                var formation = Map.Units.Formations.Single(f => f.FormationId == State.FormationId(runtime));
                var body = Map.Units.Bodies.Single(b => b.Id == formation.Layouts[0].Members[0].BodyId);
                var sourceArea = State.BattleArea(hit.AttackerId, false, from);
                var targetArea = State.BattleArea(targetId, targetSettlement, to);
                bool ranged = sourceArea is {} a && targetArea is {} b && !a.IntersectsExpanded(b, MapCoordinates.UnitsPerCell);
                if (ranged && body.RangedActions.Length > 0 || body.MeleeActions.Length == 0)
                { action = UnitVisualAction.Ranged; }
                var actions = action == UnitVisualAction.Ranged ? body.RangedActions : body.MeleeActions;
                if (actions.Length > 0) kind = actions[unit.Id % actions.Length].Projectile;
                source.Y -= body.AnchorY / 2;
            }
            else continue;
            attacks.Add(new(hit.AttackerId, hit.AttackerIsSettlement, targetId, targetSettlement,
                source.X, source.Y, target.X, target.Y, UnitVisuals.Direction(from, to), action, kind) { Path = path, Origins = origins });
        }
        _attacks = attacks.DistinctBy(a => (a.SourceId, a.SourceIsSettlement, a.TargetId, a.TargetIsSettlement, a.Projectile)).ToArray();
        foreach (var attack in _attacks.Where(a => a.Projectile != MapProjectileKind.None))
            Day.RequireDuration(TurnDayPresentation.MovementDuration + MapProjectiles.Duration(attack) / 1000 +
                (Map.Units.Projectiles[attack.Projectile].Impact is {} impact ? Clips[impact.ClipId].Frames.Length * Clips[impact.ClipId].FrameMilliseconds / 1000.0 : 0) + 0.1);
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
