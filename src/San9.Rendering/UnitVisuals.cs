using San9.Content.Maps;
using San9.Core.Domain;

namespace San9.Rendering;

public sealed record UnitVisual(int Id, double CellX, double Depth, MapAnimatedSprite[] Animations)
{
    public uint Soldiers { get; init; }
    public int Morale { get; init; }
    public string? CommanderNameKey { get; init; }
    public int LabelX { get; init; }
    public int LabelY { get; init; }
    public int BodySpriteCount { get; init; }
    public int AnchorX { get; init; }
    public int AnchorY { get; init; }
}
public sealed record UnitVisualState(int Id, double CellX, double CellY, int Elevation, uint Soldiers,
    int FormationId, UnitCondition Condition, int Direction, bool HasCityInfluence, int Color, int Glyph)
{
    public bool UsesImperialAppearance { get; init; }
    public bool IsRulerCommander { get; init; }
    public int Morale { get; init; }
    public string? CommanderNameKey { get; init; }
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
        var layout = formation.Layouts.Where(l => l.MinimumSoldiers <= state.Soldiers).MaxBy(l => l.MinimumSoldiers)!;
        var members = layout.Members.Select((member, index) => (Member: member, Index: index))
            .OrderBy(m => m.Member.Y).ThenBy(m => m.Member.X).ToArray();
        foreach (var member in members)
        {
            var body = bodies[member.Member.BodyId];
            if (state.UsesImperialAppearance && body.ImperialBodyId is {} imperial) body = bodies[imperial];
            Add(body.DirectionClips[state.Direction], member.Member.X - body.AnchorX, member.Member.Y - body.AnchorY,
                (int)(((long)state.Id + member.Index) % clips[body.DirectionClips[state.Direction]].Frames.Length));
        }
        Add($"unit-flag-{state.Direction}-pole-back", -34, formation.FlagY);
        Add($"unit-flag-{state.Direction}-cloth-{state.Color}", -34, formation.FlagY);
        int glyph = state.IsRulerCommander ? catalog.RulerFlagGlyph : state.Glyph;
        if (glyph >= 0)
        {
            int ink = new[] { 3, 6, 7, 8, 11, 16, 18, 21, 22, 23, 24, 26, 27, 29, 31, 33, 38, 39, 40, 46, 49, 50 }.Contains(state.Color) ? 1 : 0;
            Add($"unit-flag-{state.Direction}-glyph-{ink}-{glyph}", -34, formation.FlagY);
        }
        Add($"unit-flag-{state.Direction}-pole-front", -34, formation.FlagY);
        if (state.HasCityInfluence)
        {
            var effect = layout.Influence;
            Add(effect.ClipId, effect.X, effect.Y);
        }
        if (catalog.Conditions.TryGetValue(state.Condition, out var status))
            Add(status.ClipId, status.X, formation.StatusY + status.Y);
        return new(state.Id, state.CellX, state.CellX + state.CellY, animations.ToArray())
        { Soldiers = state.Soldiers, Morale = state.Morale, CommanderNameKey = state.CommanderNameKey, LabelX = x, LabelY = y - 96,
          BodySpriteCount = members.Length, AnchorX = x, AnchorY = y };
    }
}
