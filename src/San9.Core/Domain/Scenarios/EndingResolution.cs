using System.Collections.Immutable;

namespace San9.Core.Domain;

public enum EndingPolicy { ContinentalExpansion, DomesticProsperity }
public enum EndingOutcome { GreatEmpire, CommercialPower, CulturalPower, CityState, OrdinaryState, SouthernDynasty, DividedState, FallenState }

public sealed record EndingOfficer(int Id, OfficerAbilities Abilities, int Affinity, int? BloodlineId,
    int NavalProficiency, int KnowledgeProficiency, int LearnedTacticCount);
public sealed record EndingOfficeholders(
    [property: System.Text.Json.Serialization.JsonRequired] int ChancellorId,
    [property: System.Text.Json.Serialization.JsonRequired] int MarshalId,
    [property: System.Text.Json.Serialization.JsonRequired] int ProtectorId,
    [property: System.Text.Json.Serialization.JsonRequired] int SuccessorId);

/// <summary>Derived once from the victory world and content geography; never a second editable source of world state.</summary>
public sealed record EndingSnapshot(int RulerId, ImmutableArray<EndingOfficer> Officers,
    long SoldiersAndWounded, long Money, long Food, int Trust, bool HasLivingBarbarianForce,
    long ExpansionPublicOrder, bool OwnsExpansionGateways, long ExpansionCommerce,
    long DomesticDurability, bool OwnsDomesticGateways, long DomesticPublicOrder, int CulturalTreasureCount);

public sealed record EndingResult(EndingOutcome Outcome, int DecisiveStage, bool? StableSuccession);

/// <summary>Pure resolution of the two ending policies. Stage numbers describe decisions, not simulation time.</summary>
public static class EndingResolution
{
    public static EndingResult Resolve(EndingSnapshot snapshot, EndingOfficeholders? officeholders, EndingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        Validate(snapshot);
        if (snapshot.Officers.Length < 5)
        {
            if (officeholders is not null) throw new ArgumentException("An undersized court cannot select ending officeholders.", nameof(officeholders));
            return new(EndingOutcome.FallenState, 1, false);
        }
        ArgumentNullException.ThrowIfNull(officeholders);
        int[] selected = [officeholders.ChancellorId, officeholders.MarshalId, officeholders.ProtectorId, officeholders.SuccessorId];
        if (selected.Distinct().Count() != 4 || selected.Contains(snapshot.RulerId) ||
            selected.Any(id => !snapshot.Officers.Any(o => o.Id == id)))
            throw new ArgumentException("Ending officeholders must be four distinct members other than the ruler.", nameof(officeholders));
        var people = snapshot.Officers.ToDictionary(o => o.Id);
        var ruler = people[snapshot.RulerId];
        var chancellor = people[officeholders.ChancellorId];
        var marshal = people[officeholders.MarshalId];
        var protector = people[officeholders.ProtectorId];
        var successor = people[officeholders.SuccessorId];
        EndingOfficer[] ministers = [chancellor, marshal, protector];
        bool stable = chancellor.Abilities.Politics >= 80 &&
            (successor.Abilities.Politics > 70 && EconomyMath.AffinityDistance(successor.Affinity, chancellor.Affinity) <= 15 ||
             successor.BloodlineId is int family && family == ruler.BloodlineId &&
             EconomyMath.AffinityDistance(successor.Affinity, chancellor.Affinity) <= 5);
        EndingResult Failure(int stage, EndingOutcome success, EndingOutcome failure) => new(stable ? success : failure, stage, stable);

        if (policy == EndingPolicy.ContinentalExpansion)
        {
            if (snapshot.SoldiersAndWounded < 300000 || (long)marshal.Abilities.Leadership + protector.Abilities.Leadership < 150)
                return Failure(1, EndingOutcome.SouthernDynasty, EndingOutcome.FallenState);
            if (snapshot.ExpansionPublicOrder < 2000 || protector.Abilities.War < 90)
                return Failure(2, EndingOutcome.OrdinaryState, EndingOutcome.DividedState);
            if (!snapshot.OwnsExpansionGateways && (snapshot.ExpansionCommerce < 2500 || ministers.Sum(o => (long)o.NavalProficiency) < 1200))
                return Failure(3, EndingOutcome.CommercialPower, EndingOutcome.DividedState);
            return new(!snapshot.HasLivingBarbarianForce && ministers.Sum(o => (long)o.LearnedTacticCount) >= 60
                ? EndingOutcome.GreatEmpire : EndingOutcome.CommercialPower, 4, null);
        }
        if (snapshot.DomesticDurability < 1500 || ministers.Sum(o => (long)o.KnowledgeProficiency) < 600)
            return Failure(1, EndingOutcome.OrdinaryState, EndingOutcome.FallenState);
        if (snapshot.Trust < 950 || EconomyMath.AffinityDistance(ruler.Affinity, chancellor.Affinity) > 15)
            return Failure(2, EndingOutcome.OrdinaryState, EndingOutcome.DividedState);
        if (!snapshot.OwnsDomesticGateways && (snapshot.Money < 300000 || snapshot.Food < 750000 ||
            ministers.Sum(o => (long)o.Abilities.Leadership) < 240))
            return Failure(3, EndingOutcome.SouthernDynasty, EndingOutcome.FallenState);
        return new(snapshot.DomesticPublicOrder >= 4000 && snapshot.CulturalTreasureCount >= 15
            ? EndingOutcome.CulturalPower : EndingOutcome.CityState, 4, null);
    }

    private static void Validate(EndingSnapshot snapshot)
    {
        if (snapshot.Officers.IsDefaultOrEmpty || snapshot.Officers.Any(o => o is null || o.Id < 0 ||
            !o.Abilities.IsNonnegative || o.Affinity is < 0 or >= 150 || o.BloodlineId < 0 ||
            o.NavalProficiency < 0 || o.KnowledgeProficiency < 0 || o.LearnedTacticCount < 0) ||
            snapshot.Officers.Select(o => o.Id).Distinct().Count() != snapshot.Officers.Length ||
            !snapshot.Officers.Any(o => o.Id == snapshot.RulerId) || snapshot.Trust is < 0 or > 1000 ||
            snapshot.SoldiersAndWounded < 0 || snapshot.Money < 0 || snapshot.Food < 0 ||
            snapshot.ExpansionPublicOrder < 0 || snapshot.ExpansionCommerce < 0 || snapshot.DomesticDurability < 0 ||
            snapshot.DomesticPublicOrder < 0 || snapshot.CulturalTreasureCount < 0)
            throw new ArgumentException("Invalid victory snapshot.", nameof(snapshot));
    }
}
