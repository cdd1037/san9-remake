namespace San9.Core.Domain;

public sealed record ForceState(int Id, uint Money, uint Food, int Trust)
{
    public System.Collections.Immutable.ImmutableArray<int> ProvidedTactics { get; init; } = [];
    public bool IsBarbarian { get; init; }
    public int? LeaderId { get; init; }
    public int? RankId { get; init; }
}

public enum SettlementKind { City, Facility }

public sealed record SettlementState(int Id, int? OwnerId, uint Soldiers, int Durability, int MaxDurability,
    int Morale, int PublicOrder, int Commerce, int MaxCommerce, int Cultivation, int MaxCultivation,
    bool CommerceUsed = false, bool CultivationUsed = false)
{
    public SettlementKind Kind { get; init; }
    public int? CorpsId { get; init; }
    public uint Population { get; init; }
    public uint DraftPopulation { get; init; }
}

public sealed record DevelopmentCommand(int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity);
public enum DevelopmentRejection { None, UnknownObject, UnsupportedActivity, WrongOwner, OfficerUnavailable, AlreadyUsed, AtMaximum, InsufficientFunds, NotAuthorized }
public readonly record struct DevelopmentOutcome(DevelopmentRejection Rejection, int Increment = 0, int Cost = 0)
{
    public bool Accepted => Rejection == DevelopmentRejection.None;
}
