using San9.Core.Domain;

namespace San9.Content.Maps;

public sealed record MapUnitBody(string Id, string[] DirectionClips, int AnchorX, int AnchorY)
{
    public string? ImperialBodyId { get; init; }
}
public sealed record MapUnitMember(string BodyId, int X, int Y);
public sealed record MapUnitLayout(uint MinimumSoldiers, MapUnitMember[] Members, MapUnitEffect Influence);
public sealed record MapUnitFormation(int FormationId, MapUnitLayout[] Layouts, int FlagY, int StatusY);
public sealed record MapUnitEffect(string ClipId, int X, int Y);

// Complete projected layouts: ranks and influence always select the same display scale.
public sealed record MapUnitCatalog(MapUnitBody[] Bodies, MapUnitFormation[] Formations,
    Dictionary<UnitCondition, MapUnitEffect> Conditions, int RulerFlagGlyph)
{
    public static MapUnitCatalog Empty { get; } = new([], [], [], -1);

    public void Validate(IReadOnlyDictionary<string, MapAnimationClip> clips)
    {
        var bodies = Bodies.ToDictionary(b => b.Id);
        _ = Formations.ToDictionary(f => f.FormationId);
        if (RulerFlagGlyph < -1 ||
            Bodies.Any(b => string.IsNullOrWhiteSpace(b.Id) || b.DirectionClips.Length != 8 ||
                b.DirectionClips.Any(c => !clips.ContainsKey(c)) ||
                b.ImperialBodyId is {} imperial && !bodies.ContainsKey(imperial)) ||
            Formations.Any(f => f.FormationId < 0 || f.Layouts.Length == 0 ||
                !f.Layouts.Any(l => l.MinimumSoldiers == 1) ||
                f.Layouts.Select(l => l.MinimumSoldiers).Distinct().Count() != f.Layouts.Length ||
                f.Layouts.Any(l => l.MinimumSoldiers == 0 || l.Members.Length == 0 ||
                    !clips.ContainsKey(l.Influence.ClipId) ||
                    l.Members.Any(m => !bodies.ContainsKey(m.BodyId)))) ||
            Conditions.Any(c => !Enum.IsDefined(c.Key) || !clips.ContainsKey(c.Value.ClipId)))
            throw new InvalidDataException("Invalid field unit visual catalog.");
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
