using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Occurrence identities projected from committed facts, without replaying gameplay rules.</summary>
internal sealed class PresentationIdentity(StrategySnapshot before, StrategySnapshot after)
{
    private Dictionary<int, ForceState>? forces;
    private Dictionary<int, SettlementState>? settlements;
    private StrategySnapshot? projected;

    internal StrategySnapshot View => forces is null && settlements is null ? before : projected ??=
        before.ForWorld(before.World with
        {
            Forces = forces?.Values.OrderBy(f => f.Id).ToImmutableArray() ?? before.World.Forces,
            Settlements = settlements?.Values.OrderBy(s => s.Id).ToImmutableArray() ?? before.World.Settlements
        });

    internal void Apply(DomainEvent fact)
    {
        switch (fact)
        {
            case FacilityConstructionChangedEvent { Result: { } result }:
                SetSettlement(result); break;
            case SettlementNeutralizedEvent neutralized:
                SetSettlement(neutralized.Result); break;
            case SettlementCapturedEvent captured:
                SetSettlement(captured.Result); break;
            case FacilityDestroyedEvent destroyed:
                SetSettlement(destroyed.Result); break;
            case UnitAbsorbedEvent absorbed:
                SetSettlement(absorbed.Result); break;
            case ForceLeaderChangedEvent leader:
                forces ??= before.World.Forces.ToDictionary(f => f.Id);
                forces[leader.ForceId] = forces[leader.ForceId] with { LeaderId = leader.OfficerId }; break;
            case PeerageChangedEvent rank:
                forces ??= before.World.Forces.ToDictionary(f => f.Id);
                forces[rank.ForceId] = forces[rank.ForceId] with { RankId = rank.RankId, CountryTitleId = rank.CountryTitleId }; break;
            case ForceFoundedEvent founded:
                forces ??= before.World.Forces.ToDictionary(f => f.Id);
                forces[founded.ForceId] = after.World.Forces.Single(f => f.Id == founded.ForceId) with { LeaderId = founded.FounderId }; break;
            case CityIndependenceEvent independence:
                forces ??= before.World.Forces.ToDictionary(f => f.Id);
                forces[independence.ForceId] = after.World.Forces.Single(f => f.Id == independence.ForceId) with { LeaderId = independence.FounderId }; break;
            default:
                return;
        }
        projected = null;
    }

    private void SetSettlement(SettlementState value)
    {
        settlements ??= before.World.Settlements.ToDictionary(s => s.Id);
        settlements[value.Id] = value;
    }

    internal static StrategySnapshot BeforeFact(StrategySnapshot before, StrategySnapshot after,
        IReadOnlyList<DomainEvent> events, int index)
    {
        var identities = new PresentationIdentity(before, after);
        for (int i = 0; i < index; i++) identities.Apply(events[i]);
        return identities.View;
    }
}
