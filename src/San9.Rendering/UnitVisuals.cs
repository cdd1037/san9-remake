using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

public sealed record UnitVisual(int Id, double CellX, double Depth, MapAnimatedSprite[] Animations)
{
    public uint Soldiers { get; init; }
    public int Morale { get; init; }
    public int LabelX { get; init; }
    public int LabelY { get; init; }
}
public sealed record UnitVisualState(int Id, double CellX, double CellY, int Elevation, uint Soldiers,
    int FormationId, UnitCondition Condition, int Direction, bool HasCityInfluence, int Color, int Glyph)
{
    public int Morale { get; init; }
}

public static class UnitVisuals
{
    public static int Direction(WorldPosition position, WorldPosition? target)
    {
        if (target is not {} point || point == position) return 0;
        double dx = (double)point.X - position.X, dy = (double)point.Y - position.Y;
        // Clockwise screen directions: northeast, east, southeast, south, southwest, west, northwest, north.
        return ((int)Math.Round(Math.Atan2(dy, dx) * 4 / Math.PI) + 10) % 8;
    }

    public static UnitVisual Resolve(UnitVisualState state, MapUnitCatalog catalog,
        IReadOnlyDictionary<string, MapAnimationClip> clips)
    {
        if (state.Soldiers == 0 || state.Direction is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(state));
        var formation = catalog.Formations.Single(f => f.FormationId == state.FormationId);
        var bodies = catalog.Bodies.ToDictionary(b => b.Id);
        int x = (int)Math.Round(32 * (state.CellX + 200 - state.CellY) + 96);
        int y = (int)Math.Round(16 * (state.CellX + state.CellY + 1) + 112) - 10 * (state.Elevation - 1);
        List<MapAnimatedSprite> animations = [];
        void Add(string clip, int dx, int dy, int phase = 0) =>
            animations.Add(new(clip, x + dx, y + dy, phase % clips[clip].Frames.Length, -1));
        double angle = state.Direction * Math.PI / 4;
        var members = formation.Members.Where(m => m.MinimumSoldiers <= state.Soldiers).Select((m, index) =>
        {
            // Disordered units lose their regular ranks, without changing domain positions.
            int spreadX = state.Condition is UnitCondition.Confused or UnitCondition.NoFormation or UnitCondition.Routed ?
                (index * 17 % 13 - 6) : 0;
            int spreadY = state.Condition is UnitCondition.Confused or UnitCondition.NoFormation or UnitCondition.Routed ?
                (index * 11 % 13 - 6) : 0;
            double gx = (m.X + spreadX) * Math.Cos(angle) - (m.Y + spreadY) * Math.Sin(angle);
            double gy = (m.X + spreadX) * Math.Sin(angle) + (m.Y + spreadY) * Math.Cos(angle);
            return (Member: m, Index: index, X: (int)Math.Round(gx - gy), Y: (int)Math.Round((gx + gy) / 2));
        }).OrderBy(m => m.Y).ThenBy(m => m.X).ToArray();
        foreach (var member in members)
        {
            var body = bodies[member.Member.BodyId];
            Add(body.DirectionClips[state.Direction], member.X - body.AnchorX, member.Y - body.AnchorY,
                (int)(((long)state.Id + member.Index) % clips[body.DirectionClips[state.Direction]].Frames.Length));
        }
        Add("flag-pole-back", -34, formation.FlagY);
        Add($"flag-cloth-{state.Color}", -34, formation.FlagY);
        if (state.Glyph >= 0)
        {
            int ink = new[] { 3, 6, 7, 8, 11, 16, 18, 21, 22, 23, 24, 26, 27, 29, 31, 33, 38, 39, 40, 46, 49, 50 }.Contains(state.Color) ? 1 : 0;
            Add($"flag-glyph-{ink}-{state.Glyph}", -34, formation.FlagY);
        }
        Add("flag-pole-front", -34, formation.FlagY);
        if (state.HasCityInfluence)
        {
            var effect = catalog.Influence.Where(e => e.MinimumSoldiers <= state.Soldiers).MaxBy(e => e.MinimumSoldiers)!.Effect;
            Add(effect.ClipId, effect.X, effect.Y);
        }
        if (catalog.Conditions.TryGetValue(state.Condition, out var status))
            Add(status.ClipId, status.X, formation.StatusY + status.Y);
        return new(state.Id, state.CellX, state.CellX + state.CellY, animations.ToArray())
        { Soldiers = state.Soldiers, Morale = state.Morale, LabelX = x - 38, LabelY = y + members.Max(m => m.Y) + 18 };
    }
}
