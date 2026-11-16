using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentAttackRequests(DomainEvent fact)
    {
        if (fact is not AttackRequestEndedEvent ended ||
            ended.Reason is not (AttackRequestEndReason.Fulfilled or AttackRequestEndReason.Expired)) return false;
        int requested = ended.Request.RequestedForceId, requester = ended.Request.RequesterForceId;
        if (!Player(requested) && !Player(requester)) return true;
        clock = ended.Clock;
        Force("force", requested); Force("requester", requester);
        int leader = before.World.Forces.Single(f => f.Id == requested).LeaderId!.Value;
        location = PersonLocation(leader);
        if (ended.Reason == AttackRequestEndReason.Fulfilled)
        {
            colorForce = Player(requested) ? requested : requester;
            Person("actor", leader);
            Person("other", before.World.Forces.Single(f => f.Id == requester).LeaderId!.Value);
            Message("request-fulfilled", record: true);
            if (Player(requested) && ended.TrustChange > 0)
            {
                Message("request-fulfilled-trust");
                Record("request-fulfilled-trust", MapReportImportance.Detailed);
            }
        }
        else
        {
            Message("request-expired");
            Force("force", ended.LiableForceId!.Value);
            if (ended.TrustChange < 0)
            {
                Message("request-expired-trust");
                Record("request-expired-trust-record", MapReportImportance.Detailed);
            }
            if (Player(requested) && ended.LoyaltyDecreased)
            {
                Message("request-expired-loyalty");
                Record("request-expired-loyalty-record", MapReportImportance.Detailed);
            }
        }
        return true;
    }
}
