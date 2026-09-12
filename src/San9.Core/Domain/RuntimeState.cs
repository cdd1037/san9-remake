namespace San9.Core.Domain;

public sealed record ForceState(int Id, uint Money, uint Food, int Trust)
{
    [System.Text.Json.Serialization.JsonRequired] public bool IsEliminated { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int BaseTrust { get; init; }
    public System.Collections.Immutable.ImmutableArray<int> ProvidedTactics { get; init; } = [];
    public bool IsBarbarian { get; init; }
    public int? LeaderId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? AdviserId { get; init; }
    public int? RankId { get; init; }
}

public enum SettlementKind { City, Facility }

public sealed record SettlementState(int Id, int? OwnerId, uint Soldiers, int Durability, int MaxDurability,
    int Morale, int PublicOrder, int Commerce, int MaxCommerce, int Cultivation, int MaxCultivation,
    bool CommerceUsed = false, bool CultivationUsed = false)
{
    [System.Text.Json.Serialization.JsonRequired] public FacilityInstance? Facility { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsDestroyed { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int ConfusionDaysRemaining { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public uint Wounded { get; init; }
    public SettlementKind Kind { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool CanHostPersons { get; init; } = true;
    [System.Text.Json.Serialization.JsonRequired] public uint SoldierCapacity { get; init; } = 500000;
    [System.Text.Json.Serialization.JsonRequired] public bool ConsumesFood { get; init; } = true;
    [System.Text.Json.Serialization.JsonRequired] public bool PatrolUsed { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool RepairUsed { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool RecruitUsed { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool TrainUsed { get; init; }
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
