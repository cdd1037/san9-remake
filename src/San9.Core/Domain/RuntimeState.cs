namespace San9.Core.Domain;

public sealed record ForceState(int Id, uint Money, uint Food, int Trust)
{
    public int? LeaderId { get; init; }
}

public sealed record SettlementState(int Id, int? OwnerId, uint Soldiers, int Durability, int MaxDurability,
    int Morale, int PublicOrder, int Commerce, int MaxCommerce, int Cultivation, int MaxCultivation,
    bool CommerceUsed = false, bool CultivationUsed = false)
{
    public int? CorpsId { get; init; }
}

public sealed record DevelopmentCommand(int ForceId, int SettlementId, int OfficerId, DomesticActivity Activity);
public enum DevelopmentRejection { None, UnknownObject, UnsupportedActivity, WrongOwner, OfficerUnavailable, AlreadyUsed, AtMaximum, InsufficientFunds }
public readonly record struct DevelopmentOutcome(DevelopmentRejection Rejection, int Increment = 0, int Cost = 0)
{
    public bool Accepted => Rejection == DevelopmentRejection.None;
}
