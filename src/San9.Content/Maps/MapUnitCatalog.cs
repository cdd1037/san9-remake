using San9.Core.Domain;
using System.Text.Json.Serialization;

namespace San9.Content.Maps;

public sealed record MapUnitBody(string Id, string[] DirectionClips, int AnchorX, int AnchorY)
{
    public string? ImperialBodyId { get; init; }
    [JsonRequired] public MapUnitAttack? MeleeAttack { get; init; }
    [JsonRequired] public MapUnitAttack? RangedAttack { get; init; }
    [JsonRequired] public MapUnitFlagPlacement[][] Flags { get; init; } = [];
    [JsonIgnore] public IEnumerable<MapUnitAttack> Attacks => new[] { MeleeAttack, RangedAttack }.OfType<MapUnitAttack>();
    [JsonRequired] public MapUnitEffect? Wake { get; init; }
    [JsonRequired] public MapUnitEffect? Trail { get; init; }
    [JsonRequired] public double TrailChance { get; init; }
    [JsonRequired] public MapUnitParticle[] Engagement { get; init; } = [];
}
public sealed record MapUnitParticle(string ClipId, int X, int Y, int DepthOffset);
public sealed record MapUnitAction(string[] DirectionClips);
public sealed record MapUnitAttack(MapUnitAction[] Actions, MapProjectileKind Projectile, int VolleySize);
public sealed record MapUnitFlagPlacement(int X, int Y, int DepthOffset);
public enum MapProjectileKind { None, Arrow, Stone, Rockfall }
public sealed record MapProjectile(string[] Frames, int FrameMilliseconds, bool Directional, int AnchorX, int AnchorY)
{
    [JsonRequired] public MapUnitEffect? Impact { get; init; }
}
public sealed record MapUnitMember(string BodyId, int X, int Y)
{
    [JsonRequired] public int Group { get; init; }
    [JsonRequired] public int Index { get; init; }
}
public sealed record MapUnitLayout(uint MinimumSoldiers, MapUnitMember[] Members, MapUnitEffect Influence);
public sealed record MapUnitFormation(int FormationId, MapUnitLayout[] Layouts, int StatusY);
public sealed record MapUnitEffect(string ClipId, int X, int Y);

// Complete projected layouts: ranks and influence always select the same display scale.
public sealed record MapUnitCatalog(MapUnitBody[] Bodies, MapUnitFormation[] Formations,
    Dictionary<UnitCondition, MapUnitEffect> Conditions, int RulerFlagGlyph)
{
    [JsonRequired] public Dictionary<MapProjectileKind, MapProjectile> Projectiles { get; init; } = [];
    [JsonRequired] public MapUnitEffect[] VisionSlices { get; init; } = [];
    public static MapUnitCatalog Empty { get; } = new([], [], [], -1);

    public void Validate(IReadOnlyDictionary<string, MapAnimationClip> clips, IReadOnlySet<string>? assets = null)
    {
        var bodies = Bodies.ToDictionary(b => b.Id);
        _ = Formations.ToDictionary(f => f.FormationId);
        if (RulerFlagGlyph < -1 ||
            Bodies.Any(b => string.IsNullOrWhiteSpace(b.Id) || b.DirectionClips.Length != 8 ||
                b.DirectionClips.Any(c => !clips.ContainsKey(c)) ||
                b.Flags.Length != 8 || b.Flags.Any(f => f is null || f.Any(flag => flag is null)) ||
                b.Attacks.Any(a => a.Actions.Length == 0 || a.Actions.Any(v => v.DirectionClips.Length != 8 || v.DirectionClips.Any(c => !clips.ContainsKey(c))) ||
                    !Enum.IsDefined(a.Projectile) || a.Projectile == MapProjectileKind.None && a.VolleySize != 0 ||
                    a.Projectile != MapProjectileKind.None && (a.VolleySize <= 0 || !Projectiles.ContainsKey(a.Projectile))) ||
                b.Wake is {} wake && !clips.ContainsKey(wake.ClipId) ||
                b.Trail is {} trail && !clips.ContainsKey(trail.ClipId) || !double.IsFinite(b.TrailChance) || b.TrailChance is < 0 or > 100 ||
                b.Engagement.Any(e => !clips.ContainsKey(e.ClipId)) ||
                b.ImperialBodyId is {} imperial && !bodies.ContainsKey(imperial)) ||
            Formations.Any(f => f.FormationId < 0 || f.Layouts.Length == 0 ||
                !f.Layouts.Any(l => l.MinimumSoldiers == 1) ||
                f.Layouts.Select(l => l.MinimumSoldiers).Distinct().Count() != f.Layouts.Length ||
                f.Layouts.Any(l => l.MinimumSoldiers == 0 || l.Members.Length == 0 ||
                    !clips.ContainsKey(l.Influence.ClipId) ||
                    l.Members.Any(m => !bodies.ContainsKey(m.BodyId) || m.Group < 0 || m.Index < 0))) ||
            VisionSlices.Length != 0 && (VisionSlices.Length != 16 || VisionSlices.Any(s => !clips.ContainsKey(s.ClipId))) ||
            Conditions.Any(c => !Enum.IsDefined(c.Key) || !clips.ContainsKey(c.Value.ClipId)))
            throw new InvalidDataException("Invalid field unit visual catalog.");
        if (Projectiles.Any(p => p.Key == MapProjectileKind.None || !Enum.IsDefined(p.Key) || p.Value.Frames.Length == 0 ||
            p.Value.FrameMilliseconds <= 0 || p.Value.Impact is {} impact && !clips.ContainsKey(impact.ClipId) ||
            p.Value.Frames.Any(f => string.IsNullOrWhiteSpace(f) || assets is not null && !assets.Contains(f))))
            throw new InvalidDataException("Invalid map projectile catalog.");
        if (Formations.Length > 0)
            foreach (int direction in Enumerable.Range(0, 8))
            {
                string prefix = $"unit-flag-{direction}";
                if (!clips.ContainsKey($"{prefix}-pole-back") || !clips.ContainsKey($"{prefix}-pole-front") ||
                    RulerFlagGlyph >= 0 && (!clips.ContainsKey($"{prefix}-glyph-0-{RulerFlagGlyph}") ||
                        !clips.ContainsKey($"{prefix}-glyph-1-{RulerFlagGlyph}")))
                    throw new InvalidDataException("Missing directional unit flag clip.");
            }
    }
}
