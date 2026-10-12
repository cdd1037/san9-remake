using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Prepared once before playback; camera preparation never modifies this plan.</summary>
public sealed class MapDayPlan
{
    internal Dictionary<int, MapUnitMotion> Motions => _motions;
    public IReadOnlyList<MapAttackMotion> Attacks => _attacks;
    private readonly Dictionary<MapAttackMotion, double> _attackDurations = [];
    public double AttackDuration(MapAttackMotion attack) => _attackDurations[attack];
    // Attack admission uses the presented camera, independent of resource prefetch coverage.
    public static bool IsAttackVisible(MapAttackMotion attack, double x, double y, double width, double height) =>
        attack.FromX >= x - 80 && attack.FromX < x + width + 80 &&
        attack.FromY >= y - 80 && attack.FromY < y + height + 80;
    private readonly MapPackage Map;
    private readonly StrategySnapshot State;
    private readonly IReadOnlyList<DomainEvent> _events;
    private readonly IReadOnlyDictionary<int, SettlementView> Settlements;
    private readonly MapBuilding[] Buildings;
    private readonly Dictionary<int, UnitVisualState> _units;
    private readonly IReadOnlyDictionary<string, MapAnimationClip> Clips;
    private Dictionary<int, MapUnitMotion> _motions = [];
    private MapAttackMotion[] _attacks = [];
    public MapDayPlan(MapPackage map, StrategySnapshot state, IReadOnlyList<DomainEvent> events)
    {
        Map = map; State = state; _events = events;
        Settlements = state.Settlements.ToDictionary(s => s.Id);
        var projection = MapSceneProjection.Create(state, map);
        Buildings = projection.Buildings; _units = projection.Units;
        Clips = map.Animations.Clips.ToDictionary(c => c.Id);
        Prepare();
    }
    private MapMotionPoint Project(double x, double y)
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
    private MapUnitLayout AttackLayout(UnitVisualState state, WorldPosition position)
    {
        var runtime = State.World.Units.Single(u => u.Id == state.Id) with { Position = position };
        var formation = Map.Units.Formations.Single(f => f.FormationId == State.FormationId(runtime));
        return formation.Layouts.Where(l => l.MinimumSoldiers <= state.BodySoldiers).MaxBy(l => l.MinimumSoldiers)!;
    }
    private MapUnitBody AttackBody(UnitVisualState state, MapUnitMember member)
    {
        var body = Map.Units.Bodies.Single(b => b.Id == member.BodyId);
        return state.UsesImperialAppearance && body.ImperialBodyId is {} imperial
            ? Map.Units.Bodies.Single(b => b.Id == imperial) : body;
    }
    private void Prepare()
    {
        _motions = _events.OfType<UnitMovedEvent>().Where(e => e.From != e.To).GroupBy(e => e.UnitId)
            .ToDictionary(g => g.Key, g => new MapUnitMotion(g));
        List<MapAttackMotion> attacks = [];
        foreach (var hit in _events.OfType<BattleDamageEvent>().Where(e => e.TacticId is null && (e.SoldierLoss > 0 || e.DurabilityLoss > 0 || e.MoraleLoss > 0)))
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
                source = source with { Y = source.Y - 36 };
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
            else if (_units.TryGetValue(hit.AttackerId, out var unit))
            {
                var body = AttackBody(unit, AttackLayout(unit, from).Members[0]);
                var sourceArea = State.BattleArea(hit.AttackerId, false, from);
                var targetArea = State.BattleArea(targetId, targetSettlement, to);
                bool ranged = sourceArea is {} a && targetArea is {} b && !a.IntersectsExpanded(b, MapCoordinates.UnitsPerCell);
                if (ranged && body.RangedActions.Length > 0 || body.MeleeActions.Length == 0)
                { action = UnitVisualAction.Ranged; }
                var actions = action == UnitVisualAction.Ranged ? body.RangedActions : body.MeleeActions;
                if (actions.Length > 0) kind = actions[unit.Id % actions.Length].Projectile;
                source = source with { Y = source.Y - body.AnchorY / 2 };
            }
            else continue;
            attacks.Add(new(hit.AttackerId, hit.AttackerIsSettlement, targetId, targetSettlement,
                source.X, source.Y, target.X, target.Y, UnitVisuals.Direction(from, to), action, kind)
            { Path = path, Origins = origins });
        }
        _attacks = attacks.DistinctBy(a => (a.SourceId, a.SourceIsSettlement, a.TargetId, a.TargetIsSettlement, a.Projectile)).ToArray();
        foreach (var attack in _attacks)
        {
            double duration = 0;
            if (!attack.SourceIsSettlement && _units.TryGetValue(attack.SourceId, out var state))
            {
                var layout = AttackLayout(state, Position(attack.SourceId, false)!.Value);
                foreach (var member in layout.Members)
                {
                    var body = AttackBody(state, member);
                    var actions = attack.Action == UnitVisualAction.Melee ? body.MeleeActions : body.RangedActions;
                    if (actions.Length == 0) continue;
                    int seed = (int)(UnitVisuals.Seed(state.Id, member.Index,
                        unchecked((int)UnitVisuals.Seed(member.Group, 0, 4))) % (uint)actions.Length);
                    var clip = Clips[actions[seed].DirectionClips[attack.Direction]];
                    duration = Math.Max(duration, clip.Frames.Length * clip.FrameMilliseconds / 1000.0);
                }
            }
            if (attack.Projectile != MapProjectileKind.None)
                duration = Math.Max(duration, MapProjectiles.Duration(attack) / 1000 +
                    (Map.Units.Projectiles[attack.Projectile].Impact is {} impact
                        ? Clips[impact.ClipId].Frames.Length * Clips[impact.ClipId].FrameMilliseconds / 1000.0 : 0));
            _attackDurations.Add(attack, duration);
        }
    }
}
