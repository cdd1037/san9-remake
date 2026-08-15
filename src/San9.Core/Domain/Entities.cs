namespace San9.Core.Domain;

public sealed record Officer(OfficerId Id, string Name);

public sealed record Force(ForceId Id, string Name);

public sealed record Settlement(SettlementId Id, string Name, ForceId? OwnerId);

public sealed record Army(
    ArmyId Id,
    ForceId OwnerId,
    SettlementId OriginId,
    SettlementId DestinationId);

public sealed record Formation(FormationId Id, string Name);

public sealed record Tactic(TacticId Id, string Name);

public sealed record Facility(FacilityId Id, string Name, SettlementId SettlementId);
