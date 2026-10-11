using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReformUnitCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId,
    int FormationId, ImmutableArray<int> OfficerIds, MilitaryOrder? Order) : GameCommand(CommandId, IssuerId, Sequence)
{
    [JsonRequired] public int? NavalFormationId { get; init; }
    [JsonRequired] public bool ConfirmAttack { get; init; }
}
public sealed record UnitReformedEvent(GameClock Clock, int UnitId, int PreviousFormationId, int FormationId,
    ImmutableArray<int> OfficerIds, int? PreviousNavalFormationId, int? NavalFormationId) : DomainEvent;
