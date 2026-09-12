using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record OfficerView(int Id, string NameKey, int? OwnerId, int? CorpsId, int? SettlementId,
    bool HasCityPrefix, bool IsAvailable, OfficerAbilities Abilities)
{
    public string? CourtesyNameKey { get; init; }
    public string? BiographyKey { get; init; }
    public OfficerStatus Status { get; init; } = OfficerStatus.Serving;
    public OfficerJourneyView? Journey { get; init; }
    public bool IsServing => Status <= OfficerStatus.Serving;
}

public sealed record OfficerJourneyView(Guid TaskId, string Kind, int OriginSettlementId, int DestinationPlaceId,
    int? TargetOfficerId, San9.Core.Simulation.GameClock Due, int ProgressDays);
