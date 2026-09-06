using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record OfficerView(int Id, string NameKey, int? OwnerId, int? CorpsId, int SettlementId,
    bool HasCityPrefix, bool IsAvailable, OfficerAbilities Abilities);
