using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum ForceCreationKind { EmptyCityUprising, CityIndependence }
public sealed record ForceOrigin(
    [property: JsonRequired] ForceCreationKind Kind,
    [property: JsonRequired] int FounderId,
    [property: JsonRequired] int SettlementId,
    [property: JsonRequired] San9.Core.Simulation.GameClock Clock)
{
    [JsonRequired] public int? PredecessorId { get; init; }
}

public sealed record ForceState(int Id, uint Money, uint Food, int Trust)
{
    [JsonRequired] public ForceOrigin? Origin { get; init; }
    [JsonRequired] public bool IsEliminated { get; init; }
    [JsonRequired] public int BaseTrust { get; init; }
    public System.Collections.Immutable.ImmutableArray<int> ProvidedTactics { get; init; } = [];
    public bool IsBarbarian { get; init; }
    public int? LeaderId { get; init; }
    [JsonRequired] public int? AdviserId { get; init; }
    public int? RankId { get; init; }
    [JsonRequired] public int? CountryTitleId { get; init; }
}

public enum SettlementKind { City, Facility }

public sealed record SettlementState(int Id, int? OwnerId, uint Soldiers, int Durability, int MaxDurability,
    int Morale, int PublicOrder, int Commerce, int MaxCommerce, int Cultivation, int MaxCultivation,
    bool CommerceUsed = false, bool CultivationUsed = false)
{
    [JsonRequired] public FacilityInstance? Facility { get; init; }
    [JsonRequired] public bool IsDestroyed { get; init; }
    [JsonRequired] public int ConfusionDaysRemaining { get; init; }
    [JsonRequired] public uint Wounded { get; init; }
    public SettlementKind Kind { get; init; }
    [JsonRequired] public bool CanHostPersons { get; init; } = true;
    [JsonRequired] public uint SoldierCapacity { get; init; } = 500000;
    [JsonRequired] public bool ConsumesFood { get; init; } = true;
    [JsonRequired] public bool PatrolUsed { get; init; }
    [JsonRequired] public bool RepairUsed { get; init; }
    [JsonRequired] public bool RecruitUsed { get; init; }
    [JsonRequired] public bool TrainUsed { get; init; }
    public int? CorpsId { get; init; }
    public uint Population { get; init; }
    public uint DraftPopulation { get; init; }
}

public sealed record DevelopmentCommand(int ForceId, int SettlementId, System.Collections.Immutable.ImmutableArray<int> OfficerIds, DomesticActivity Activity);
public enum DevelopmentRejection { None, UnknownObject, UnsupportedActivity, WrongOwner, OfficerUnavailable, AlreadyUsed, AtMaximum, InsufficientFunds, NotAuthorized, InvalidParticipants, NoRecruits, NoSoldiers }
public readonly record struct DevelopmentOutcome(DevelopmentRejection Rejection, int Increment = 0, int Cost = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Accepted => Rejection == DevelopmentRejection.None;
}
