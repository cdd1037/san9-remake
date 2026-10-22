using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentAlliances(DomainEvent fact)
    {
        switch (fact)
        {
            case AllianceEndedEvent ended when ended.Reason is not (AllianceEndReason.SubjugationSucceeded or AllianceEndReason.Betrayal):
                clock = ended.Clock; Force("target-force", ended.TargetForceId);
                Force("leader-force", ended.Alliance.LeaderForceId);
                Message(ended.Reason switch
                {
                    AllianceEndReason.LeaderEliminated => "alliance-leader-defeated",
                    AllianceEndReason.LeaderDied => "alliance-leader-died",
                    AllianceEndReason.TargetEliminated => "alliance-target-defeated",
                    AllianceEndReason.TargetDied => "alliance-target-died",
                    AllianceEndReason.Expired => "alliance-expired",
                    _ => "alliance-insufficient"
                });
                Record("alliance-ended-record");
                break;
            case AllianceVictoryEvent victory:
                clock = victory.Clock; Force("target-force", victory.TargetForceId); Force("force", victory.VictorForceId);
                int victor = before.World.Forces.Single(f => f.Id == victory.VictorForceId).LeaderId!.Value;
                Person("actor", victor); location = PersonLocation(victor); colorForce = victory.VictorForceId;
                Message("alliance-victory"); Message("alliance-victory-declaration"); Message("alliance-victory-trust"); Message("alliance-victory-ended", record: true); break;
            case AllianceFormedEvent formed when before.World.Simulation.PendingEvent is not AllianceEventSession:
                clock = formed.Clock; Force("leader-force", formed.Alliance.LeaderForceId); Force("target-force", formed.Alliance.TargetForceId);
                int allianceLeader = before.World.Forces.Single(f => f.Id == formed.Alliance.LeaderForceId).LeaderId!.Value;
                Person("actor", allianceLeader); location = PersonLocation(allianceLeader); colorForce = formed.Alliance.LeaderForceId;
                Message("alliance-formed-image"); Message("alliance-formed-declaration"); Message("alliance-formed-trust"); Message("alliance-formed-result", record: true); break;
            case AllianceFormationFailedEvent failed when before.World.Simulation.PendingEvent is not AllianceEventSession:
                clock = failed.Clock; Force("target-force", failed.TargetForceId); Message("alliance-formation-failed", record: true); break;
            case WarDeclaredEvent { BetrayedAlliance: true } betrayal:
                clock = betrayal.Clock;
                var betrayedAlliance = AllianceAtOccurrence()
                    ?? throw new InvalidDataException("Alliance betrayal has no preceding alliance context.");
                Force("target-force", betrayedAlliance.TargetForceId); Force("force", betrayal.AttackerForceId);
                Message("alliance-betrayed", record: true);
                Message(betrayedAlliance.LeaderForceId == betrayal.AttackerForceId ? "alliance-leader-betrayed" :
                    betrayedAlliance.MemberForceIds.Length < 3 ? "alliance-member-ended" : "alliance-member-left");
                Message("alliance-betrayal-trust");
                if (events.OfType<OfficerLoyaltyChangedEvent>().Any(e => e.ForceId == betrayal.AttackerForceId && e.Reason == "forced-attack" && e.Current < e.Previous))
                    Message("alliance-betrayal-loyalty");
                break;
            default: return false;
        }
        return true;
    }

    private AllianceState? AllianceAtOccurrence()
    {
        var alliance = before.World.Simulation.Alliance;
        AllianceState? beforeBetrayal = null;
        for (int i = 0; i < index; i++)
        {
            switch (events[i])
            {
                case AllianceFormedEvent formed:
                    alliance = formed.Alliance;
                    beforeBetrayal = null;
                    break;
                case AllianceMemberRemovedEvent removed when alliance is not null:
                    beforeBetrayal = alliance;
                    alliance = alliance with { MemberForceIds = alliance.MemberForceIds.Remove(removed.ForceId) };
                    break;
                case AllianceEndedEvent ended:
                    beforeBetrayal = ended.Reason == AllianceEndReason.Betrayal ? alliance : null;
                    alliance = null;
                    break;
                case WarDeclaredEvent { BetrayedAlliance: true }:
                    beforeBetrayal = null;
                    break;
            }
        }
        // The domain publishes membership removal/ending before its war declaration.
        return events[index] is WarDeclaredEvent { BetrayedAlliance: true } ? beforeBetrayal ?? alliance : alliance;
    }
}
