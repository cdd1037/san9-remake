using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum UnitPurpose { Combat, Construction, Transport }

/// <summary>Content-owned formations for a unit whose purpose does not permit formation selection.</summary>
public sealed record FixedUnitFormations(int LandFormationId, [property: JsonRequired] int? NavalFormationId);

public sealed partial class MilitaryRulebook
{
    public FixedUnitFormations? ConstructionFormations { get; }
    public FixedUnitFormations? TransportFormations { get; }

    // The fixed land formation persists through movement, return and idle orders. No second mutable purpose state is needed.
    public UnitPurpose Purpose(int formationId) => formationId == ConstructionFormations?.LandFormationId
        ? UnitPurpose.Construction : formationId == TransportFormations?.LandFormationId ? UnitPurpose.Transport : UnitPurpose.Combat;

    public bool IsFixedFormation(int id) => ConstructionFormations is { } construction &&
        (id == construction.LandFormationId || id == construction.NavalFormationId) || TransportFormations is { } transport &&
        (id == transport.LandFormationId || id == transport.NavalFormationId);

    public bool CanSelectFormation(ForceState force, int id) => !IsFixedFormation(id) &&
        Formations.TryGetValue(id, out var formation) && formation.IsCandidateFor(force);

    public bool ValidUnitFormations(int formationId, int? navalFormationId)
    {
        if (!ValidFormationPair(formationId, navalFormationId)) return false;
        var fixedPair = Purpose(formationId) switch
        {
            UnitPurpose.Construction => ConstructionFormations,
            UnitPurpose.Transport => TransportFormations,
            _ => null
        };
        return fixedPair is not null ? navalFormationId == fixedPair.NavalFormationId :
            !IsFixedFormation(formationId) && (navalFormationId is not int naval || !IsFixedFormation(naval));
    }

    public bool AllowsOrder(int formationId, MilitaryOrder? order) => order is null || order.Kind switch
    {
        MilitaryOrderKind.Transport => Purpose(formationId) == UnitPurpose.Transport,
        MilitaryOrderKind.Construct => Purpose(formationId) == UnitPurpose.Construction,
        MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement => Purpose(formationId) == UnitPurpose.Combat,
        _ => true
    };

    private void ValidateFixedFormations()
    {
        var identities = new HashSet<int>();
        foreach (var pair in new[] { ConstructionFormations, TransportFormations })
        {
            if (pair is null) continue;
            if (!Formations.TryGetValue(pair.LandFormationId, out var land) || land.TravelMode != TravelMode.Land ||
                !ValidFormationPair(pair.LandFormationId, pair.NavalFormationId) || !identities.Add(pair.LandFormationId) ||
                pair.NavalFormationId is int naval && !identities.Add(naval))
                throw new ArgumentException("Invalid fixed unit formations.");
        }
        if (BasicNavalFormationId is int basic && IsFixedFormation(basic))
            throw new ArgumentException("Default combat vessel cannot be a fixed-purpose formation.");
    }
}
