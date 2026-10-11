using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

public sealed record UnitVisual(int Id, double CellX, double Depth, MapAnimatedSprite[] Animations)
{
    public UnitVisualAction Action { get; init; }
    public int Direction { get; init; }
    public uint Soldiers { get; init; }
    public int Morale { get; init; }
    public string? CommanderNameKey { get; init; }
    public int LabelX { get; init; }
    public int LabelY { get; init; }
    public int AnchorX { get; init; }
    public int AnchorY { get; init; }
}
public sealed record UnitVisualState(int Id, double CellX, double CellY, int Elevation, uint Soldiers,
    int FormationId, UnitCondition Condition, int Direction, bool HasCityInfluence, int Color, int Glyph)
{
    // A terminal occurrence retains its last living pose until the outcome is presented.
    public uint? RetainedBodySoldiers { get; init; }
    public uint BodySoldiers => RetainedBodySoldiers ?? Soldiers;
    public UnitVisualAction Action { get; init; }
    public bool IsEngaged { get; init; }
    public bool IsMoving { get; init; }
    public bool ReceivesPositionUpdates { get; init; }
    public int VisionAngleDegrees { get; init; }
    public bool UsesImperialAppearance { get; init; }
    public bool IsRulerCommander { get; init; }
    public int Morale { get; init; }
    public string? CommanderNameKey { get; init; }
}
public enum UnitVisualAction { Idle, Moving, Melee, Ranged }

public static class UnitVisuals
{
    public static int Direction(WorldPosition position, WorldPosition? target)
    {
        if (target is not {} point || point == position) return 0;
        double dx = (double)point.X - position.X, dy = (double)point.Y - position.Y;
        // Clockwise screen directions: northeast, east, southeast, south, southwest, west, northwest, north.
        return ((int)Math.Round(Math.Atan2(dy, dx) * 4 / Math.PI) + 10) % 8;
    }

    public static uint Seed(int unit, int member, int tick)
    {
        unchecked
        {
            uint x = (uint)unit * 747796405u + (uint)member * 2891336453u + (uint)tick * 277803737u;
            x = (x ^ (x >> 16)) * 2246822519u;
            return x ^ (x >> 13);
        }
    }

    public static double Disorder(UnitVisualState state, MapUnitMember member) =>
        Math.Clamp((100 - state.Morale) * (member.Group == 0 ? 2 : member.Index == 0 ? 0 : 1), 0, 100) / 100.0;

    public static UnitVisual Resolve(UnitVisualState state, MapUnitCatalog catalog,
        IReadOnlyDictionary<string, MapAnimationClip> clips, UnitMotionPlayback? playback = null,
        Func<double, double, double>? elevationAt = null)
    {
        if (state.BodySoldiers == 0 || state.Direction is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(state));
        var formation = catalog.Formations.Single(f => f.FormationId == state.FormationId);
        var bodies = catalog.Bodies.ToDictionary(b => b.Id);
        var layout = formation.Layouts.Where(l => l.MinimumSoldiers <= state.BodySoldiers).MaxBy(l => l.MinimumSoldiers)!;
        static (double X, double Y) Ground(UnitVisualState s) => (32 * (s.CellX + 200 - s.CellY) + 96, 16 * (s.CellX + s.CellY + 1) + 112);
        var anchor = Ground(state);
        int x = (int)Math.Round(anchor.X), y = (int)Math.Round(anchor.Y - 10 * ((elevationAt?.Invoke(anchor.X, anchor.Y) ?? state.Elevation) - 1));
        uint MemberSeed(MapUnitMember m, int tick) => Seed(state.Id, m.Index, unchecked((int)Seed(m.Group, 0, tick)));
        // Resource planning also needs an unanimated snapshot, independent of any retained motion layout.
        if (playback is null)
        {
            playback = new();
            playback.Begin(state, catalog, 0, 0, false);
        }
        (double X, double Y, double GroundY, int Direction) Pose(MapUnitMember m)
        {
            var point = playback.Position(m);
            return (point.X, point.Y - 10 * ((elevationAt?.Invoke(point.X, point.Y) ?? state.Elevation) - 1), point.Y,
                playback.Direction(m, state));
        }
        List<MapAnimatedSprite> animations = [];
        void Add(string clip, double px, double py, double depth, int phase = 0, int? frame = null) =>
            animations.Add(new(clip, (int)Math.Round(px), (int)Math.Round(py), phase % clips[clip].Frames.Length, -1)
                { Depth = depth, Frame = frame });
        var members = layout.Members.Select(m => (Member: m, Pose: Pose(m)))
            .OrderBy(m => m.Pose.GroundY).ThenBy(m => m.Pose.X).ToArray();
        foreach (var m in members)
            if (bodies[m.Member.BodyId].Wake is {} wake) Add(wake.ClipId, m.Pose.X + wake.X, m.Pose.Y + wake.Y, m.Pose.GroundY + 20000);
        if (state.Action == UnitVisualAction.Moving && !state.IsEngaged && catalog.VisionSlices.Length > 0)
        {
            int sectors = Math.Clamp(state.VisionAngleDegrees / 45, 0, 8);
            int start = (2 * state.Direction - sectors + 17) % 16;
            for (int i = 0; i < 2 * sectors; i++)
            {
                var slice = catalog.VisionSlices[(start + i) % 16];
                Add(slice.ClipId, x + slice.X, y + slice.Y, anchor.Y + 20000);
            }
        }
        foreach (var m in members)
        {
            var body = bodies[m.Member.BodyId];
            if (state.UsesImperialAppearance && body.ImperialBodyId is {} imperial) body = bodies[imperial];
            var actions = state.Action == UnitVisualAction.Melee ? body.MeleeActions : state.Action == UnitVisualAction.Ranged ? body.RangedActions : [];
            bool attacking = state.Action is UnitVisualAction.Melee or UnitVisualAction.Ranged;
            int direction = attacking ? state.Direction : m.Pose.Direction;
            string clip = actions.Length == 0 ? body.DirectionClips[direction] : actions[(int)(MemberSeed(m.Member, 4) % (uint)actions.Length)].DirectionClips[direction];
            Add(clip, m.Pose.X - body.AnchorX, m.Pose.Y - body.AnchorY, m.Pose.GroundY + 30000,
                attacking ? 0 : (int)(MemberSeed(m.Member, 5) % (uint)clips[clip].Frames.Length));
            animations[^1] = animations[^1] with
            {
                IsBody = true,
                GroundX = m.Pose.X,
                GroundY = m.Pose.GroundY,
                Frame = attacking ? null : playback.Frame(m.Member, clips[clip].Frames.Length)
            };
        }
        foreach (var m in members)
        {
            var body = bodies[m.Member.BodyId];
            if (state.IsEngaged && (m.Member.Index == 0 || MemberSeed(m.Member, 2) % 3 == 0))
                foreach (var e in body.Engagement)
                    Add(e.ClipId, m.Pose.X + e.X, m.Pose.Y + e.Y, m.Pose.GroundY + 30000 + e.DepthOffset,
                        (int)(MemberSeed(m.Member, 3) % (uint)clips[e.ClipId].Frames.Length));
        }
        int glyph = state.IsRulerCommander ? catalog.RulerFlagGlyph : state.Glyph;
        foreach (string layer in FlagVisuals.Layers(state.Direction, state.Color, glyph))
            Add(layer, x - 34, y + formation.FlagY, anchor.Y + 30100);
        if (state.HasCityInfluence) Add(layout.Influence.ClipId, x + layout.Influence.X, y + layout.Influence.Y, anchor.Y + 30032);
        if (catalog.Conditions.TryGetValue(state.Condition, out var status))
            Add(status.ClipId, x + status.X, y + formation.StatusY + status.Y, anchor.Y + 40000);
        foreach (var trail in playback.Trails)
        {
            var clip = clips[trail.ClipId];
            int frame = (int)((playback.VisualTime - trail.Emitted) / clip.FrameMilliseconds);
            if (frame >= clip.Frames.Length) continue;
            double height = elevationAt?.Invoke(trail.X, trail.Y) ?? trail.Elevation;
            Add(trail.ClipId, trail.X, trail.Y - 10 * (height - 1), trail.Depth, frame: frame);
        }
        return new(state.Id, state.CellX, state.CellX + state.CellY, animations.ToArray())
        {
            Action = state.Action,
            Direction = state.Direction,
            Soldiers = state.Soldiers,
            Morale = state.Morale,
            CommanderNameKey = state.CommanderNameKey,
            LabelX = x,
            LabelY = y - 96,
            AnchorX = x,
            AnchorY = y
        };
    }
}
