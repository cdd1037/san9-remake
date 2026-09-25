using San9.Core.Domain;

namespace San9.Content.Maps;

public sealed record MapUnitBody(string Id, string[] DirectionClips, int AnchorX, int AnchorY);
public sealed record MapUnitMember(uint MinimumSoldiers, string BodyId, int X, int Y);
public sealed record MapUnitFormation(int FormationId, MapUnitMember[] Members, int FlagY, int StatusY);
public sealed record MapUnitEffect(string ClipId, int X, int Y);
public sealed record MapUnitInfluence(uint MinimumSoldiers, MapUnitEffect Effect);

// Authored display definitions. Soldier thresholds never limit domain population or identities.
public sealed record MapUnitCatalog(MapUnitBody[] Bodies, MapUnitFormation[] Formations,
    Dictionary<UnitCondition, MapUnitEffect> Conditions, MapUnitInfluence[] Influence)
{
    public static MapUnitCatalog Empty { get; } = new([], [], [], []);

    public void Validate(IReadOnlyDictionary<string, MapAnimationClip> clips)
    {
        var bodies = Bodies.ToDictionary(b => b.Id);
        _ = Formations.ToDictionary(f => f.FormationId);
        if (Bodies.Any(b => string.IsNullOrWhiteSpace(b.Id) || b.DirectionClips.Length != 8 ||
                b.DirectionClips.Any(c => !clips.ContainsKey(c))) ||
            Formations.Any(f => f.FormationId < 0 || f.Members.Length == 0 ||
                !f.Members.Any(m => m.MinimumSoldiers == 1) ||
                f.Members.Any(m => m.MinimumSoldiers == 0 || !bodies.ContainsKey(m.BodyId))) ||
            Conditions.Any(c => !Enum.IsDefined(c.Key) || !clips.ContainsKey(c.Value.ClipId)) ||
            Influence.Any(e => e.MinimumSoldiers == 0 || !clips.ContainsKey(e.Effect.ClipId)) ||
            Influence.Select(e => e.MinimumSoldiers).Distinct().Count() != Influence.Length ||
            Formations.Length > 0 && !Influence.Any(e => e.MinimumSoldiers == 1))
            throw new InvalidDataException("Invalid field unit visual catalog.");
    }
}
