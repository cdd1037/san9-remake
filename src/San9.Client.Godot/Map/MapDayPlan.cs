using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Prepared once before playback; camera preparation never modifies this plan.</summary>
public sealed class MapDayPlan
{
    internal Dictionary<int, MapUnitMotion> Motions => _motions;
    public IReadOnlyList<MapAttackMotion> Attacks => _attacks;
    private readonly Dictionary<MapAttackMotion, (double Duration, double Blocking)> _bodies = [];
    public double BodyDuration(MapAttackMotion attack) => _bodies[attack].Duration;
    public bool AdmitMapEffect(BattleObjectContext target, BattleSprite sprite, ImageAsset asset, MapPresentationBounds viewport)
    {
        if (!target.IsSettlement && target.IsGarrison) return false;
        var position = target.Position ?? (target.ServicePlaceId is int servicePlace && Settlements.TryGetValue(servicePlace, out var place)
            ? MapCoordinates.Center(new(place.X, place.Y)) : (WorldPosition?)null);
        if (position is not { } point) return false;
        var origin = Project(point.X, point.Y);
        if (!viewport.ContainsBody(origin)) return false;
        double width = asset.Width / (double)sprite.Columns;
        double left = origin.X + sprite.OffsetX - (sprite.Centered ? width / 2 : 0);
        double top = origin.Y + sprite.OffsetY;
        return viewport.Intersects(left, top, left + width, top + asset.Height, 192, 96);
    }
    public MapAttackAdmission Admit(MapAttackMotion attack, MapPresentationBounds viewport)
    {
        bool body = !attack.SourceIsSettlement && viewport.ContainsBody(attack.BodyPosition);
        // Unit shots require an attached emitting body; facilities can emit across the viewport edge.
        var shots = attack.Projectile == MapProjectileKind.None || !attack.SourceIsSettlement && !body ? [] :
            Enumerable.Range(0, MapProjectiles.ShotCount(attack)).Where(i => MapProjectiles.IsVisible(attack, i, viewport)).ToHashSet();
        double impact = attack.Projectile != MapProjectileKind.None && Map.Units.Projectiles[attack.Projectile].Impact is {} effect
            ? Clips[effect.ClipId].Frames.Length * Clips[effect.ClipId].FrameMilliseconds / 1000.0 : 0;
        double projectileBlocking = shots.Select(i => MapProjectiles.Duration(attack, i) / 1000 + impact).DefaultIfEmpty(0).Max();
        return new(body, shots, body ? _bodies[attack].Blocking : 0, projectileBlocking);
    }
    private readonly MapPackage Map;
    private readonly StrategySnapshot State;
    private readonly IReadOnlyList<DomainEvent> _events;
    private readonly IReadOnlyDictionary<int, SettlementView> Settlements;
    private readonly MapBuilding[] Buildings;
    private readonly Dictionary<int, UnitVisualState> _units;
    private readonly IReadOnlyDictionary<string, MapAnimationClip> Clips;
    private readonly IReadOnlyDictionary<string, ImageAsset> Assets;
    private Dictionary<int, MapUnitMotion> _motions = [];
    private MapAttackMotion[] _attacks = [];
    internal MapSceneProjection Projection { get; }
    public MapDayPlan(MapPackage map, StrategySnapshot state, IReadOnlyList<DomainEvent> events)
        : this(map, state, events, MapSceneProjection.Create(state, map)) { }
    internal MapDayPlan(MapPackage map, StrategySnapshot state, IReadOnlyList<DomainEvent> events, MapSceneProjection projection)
    {
        Map = map; State = state; _events = events;
        Settlements = state.Settlements.ToDictionary(s => s.Id);
        Projection = projection;
        Buildings = projection.Buildings; _units = projection.Units;
        Clips = map.Animations.Clips.ToDictionary(c => c.Id);
        Assets = map.Assets.ToDictionary(a => a.Id);
        Prepare();
    }
    private MapMotionPoint Project(double x, double y)
    {
        double cx = (x - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        double cy = (y - MapCoordinates.UnitsPerCell / 2) / MapCoordinates.UnitsPerCell;
        int cellX = Math.Clamp((int)x / MapCoordinates.UnitsPerCell, 0, Map.Width - 1);
        int cellY = Math.Clamp((int)y / MapCoordinates.UnitsPerCell, 0, Map.Height - 1);
        return new((float)(32 * (cx + Map.Height - cy) + 96),
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
        foreach (var (fact, factIndex) in _events.Select((e, i) => (e, i)))
        {
            if (fact is not BattleDamageEvent { TacticId: null } hit || hit.SoldierLoss == 0 && hit.DurabilityLoss == 0 && hit.MoraleLoss == 0) continue;
            bool targetSettlement = hit.SettlementId is not null;
            int targetId = hit.TargetUnitId ?? hit.SettlementId!.Value;
            if (Position(hit.AttackerId, hit.AttackerIsSettlement) is not {} from || Position(targetId, targetSettlement) is not {} to) continue;
            var source = Project(from.X, from.Y); var target = Project(to.X, to.Y);
            var bodyPosition = source;
            var timing = new MapAttackTiming(0, 0);
            int spacing = 200;
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
                var layout = AttackLayout(unit, from);
                var primary = layout.Members[0];
                var body = AttackBody(unit, primary);
                var sourceArea = State.BattleArea(hit.AttackerId, false, from);
                var targetArea = State.BattleArea(targetId, targetSettlement, to);
                bool ranged = sourceArea is {} a && targetArea is {} b && !a.IntersectsExpanded(b, MapCoordinates.UnitsPerCell);
                if (ranged && body.RangedAttack is not null || body.MeleeAttack is null)
                { action = UnitVisualAction.Ranged; }
                var attack = UnitVisuals.Attack(body, action);
                if (attack is null) continue;
                timing = attack.Timings[UnitVisuals.Seed(unit.Id, targetId, unchecked((int)State.World.Revision)) % (uint)attack.Timings.Length];
                spacing = attack.ShotSpacingMilliseconds;
                kind = attack.Projectile;
                if (kind != MapProjectileKind.None)
                {
                    // Each participating group's first body owns its emission point. A single
                    // body repeats the volley there; projectile choice never selects a body variant.
                    var leaders = layout.Members.Where(m => m.BodyId == primary.BodyId).GroupBy(m => m.Group)
                        .OrderBy(g => g.Key).Select(g => g.MinBy(m => m.Index)!).ToArray();
                    int direction = UnitVisuals.Direction(from, to);
                    var ground = source;
                    origins = Enumerable.Range(0, attack.VolleySize).Select(i =>
                    {
                        var member = leaders[i % leaders.Length];
                        var memberBody = AttackBody(unit, member);
                        var clip = UnitVisuals.SelectAction(memberBody, action, unit.Id, member)!.DirectionClips[direction];
                        var image = Assets[Clips[clip].Frames[0]];
                        return new MapMotionPoint(ground.X + member.X - memberBody.AnchorX + image.Width / 2,
                            ground.Y + member.Y - memberBody.AnchorY + image.Height / 2);
                    }).ToArray();
                    source = origins[0];
                }
            }
            else continue;
            attacks.Add(new(hit.AttackerId, hit.AttackerIsSettlement, targetId, targetSettlement,
                source.X, source.Y, target.X, target.Y, UnitVisuals.Direction(from, to), action, kind)
            { Path = path, Origins = origins, FactIndex = factIndex, BodyPosition = bodyPosition,
                BodyDelayMilliseconds = timing.BodyDelayMilliseconds, ProjectileDelayMilliseconds = timing.ProjectileDelayMilliseconds,
                ShotSpacingMilliseconds = spacing });
        }
        _attacks = attacks.ToArray();
        foreach (var attack in _attacks)
        {
            double duration = 0, blocking = 0;
            if (!attack.SourceIsSettlement && _units.TryGetValue(attack.SourceId, out var state))
            {
                var layout = AttackLayout(state, Position(attack.SourceId, false)!.Value);
                foreach (var member in layout.Members)
                {
                    var body = AttackBody(state, member);
                    var selected = UnitVisuals.SelectAction(body, attack.Action, state.Id, member);
                    if (selected is null) continue;
                    var clip = Clips[selected.DirectionClips[attack.Direction]];
                    double end = (attack.BodyDelayMilliseconds + (double)clip.Frames.Length * clip.FrameMilliseconds) / 1000;
                    duration = Math.Max(duration, end);
                    if (selected.BlocksDayProgress) blocking = Math.Max(blocking, end);
                }
            }
            _bodies.Add(attack, (duration, blocking));
        }
    }
}

public sealed record MapAttackAdmission(bool Body, IReadOnlySet<int> Shots, double BodyBlockingDuration, double ProjectileBlockingDuration)
{
    public double BlockingDuration => Math.Max(BodyBlockingDuration, ProjectileBlockingDuration);
}
