using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentForces(DomainEvent fact)
    {
        switch (fact)
        {
            case ForceFoundedEvent founded:
                clock = founded.Clock; Founded(founded.ForceId, founded.FounderId, founded.SettlementId, null); break;
            case CityIndependenceEvent independence:
                clock = independence.Clock; Founded(independence.ForceId, independence.FounderId, independence.SettlementId, independence.PreviousForceId); break;
            case OfficerDepartedEvent departed when Player(departed.PreviousForceId) && !departed.ServicePlaceLost:
                clock = departed.Clock; Person("actor", departed.OfficerId); Force("force", departed.PreviousForceId);
                location = PlaceLocation(departed.OriginId); Message("officer-departed", focus: true, record: true); break;
            case SettlementDefectedEvent defected when Player(defected.PreviousForceId) || Player(defected.ForceId):
                clock = defected.Clock; Force("force", defected.ForceId); Place(defected.SettlementId); location = PlaceLocation(defected.SettlementId); colorForce = defected.ForceId;
                if (defected.CommanderId is int defector)
                {
                    Person("actor", defector); Message("defection-declaration", speech: true, focus: true); Message("settlement-defection", record: true);
                    foreach (int follower in defected.FollowerIds.Where(id => id != defector)) { Person("actor", follower); Message("defection-follower"); }
                }
                else Message("abandoned-defection", focus: true, record: true);
                break;
            case UnitDefectedEvent defected when Player(defected.PreviousForceId) || Player(defected.ForceId):
                clock = defected.Clock; Force("force", defected.ForceId); Person("actor", defected.CommanderId); location = PersonLocation(defected.CommanderId); colorForce = defected.ForceId;
                Message("defection-declaration", speech: true, focus: true); Message("unit-defection", record: true);
                foreach (int follower in defected.FollowerIds.Where(id => id != defected.CommanderId)) { Person("actor", follower); Message("defection-follower"); }
                break;
            case ForcesConsolidatedEvent merged:
                clock = merged.Clock; Force("force", merged.SurvivorId); Force("previous-force", merged.AbsorbedId);
                int survivor = before.World.Forces.Single(f => f.Id == merged.SurvivorId).LeaderId!.Value;
                int absorbed = before.World.Forces.Single(f => f.Id == merged.AbsorbedId).LeaderId!.Value;
                Person("actor", survivor); Person("other", absorbed); colorForce = merged.SurvivorId;
                var ruler = before.World.Officers.Single(p => p.Id == survivor);
                int place = ruler.SettlementId ?? before.World.Units.Single(u => u.Id == ruler.UnitId).ServicePlaceId!.Value;
                Place(place); location = PersonLocation(survivor);
                Message("consolidation-arrival", focus: true);
                Message("consolidation-offer", speech: true); Message("consolidation-reply", speech: true);
                values["year"] = new(clock.Year); values["month"] = new(clock.Month);
                Message("consolidation-result", record: true); Message("consolidation-trust"); Message("consolidation-soldiers"); Message("consolidation-morale"); break;
            case ForceEliminatedEvent eliminated:
                clock = eliminated.Clock; Force("force", eliminated.ForceId);
                values["year"] = new(clock.Year); values["month"] = new(clock.Month);
                Message("force-eliminated", music: "force-eliminated-music"); break;
            case ForceLeaderChangedEvent succession when succession.PreviousId is int previous:
                if (CompositeDeath(previous)) break;
                clock = succession.Clock; Person("actor", succession.OfficerId); Person("other", previous);
                location = PersonLocation(succession.OfficerId); colorForce = succession.ForceId;
                Message("successor-declaration", speech: true);
                // In the main map the source dispatches 9992 as a record instead of also opening 7190.
                break;
            case OfficerDiedEvent death when death.Cause is not (OfficerDeathCause.NoRetreat or OfficerDeathCause.ForceEliminated):
                if (CompositeDeath(death.OfficerId) || events.OfType<EmperorDeathEvent>().Any(e => e.OfficerId == death.OfficerId)) break;
                clock = death.Clock; Person("actor", death.OfficerId);
                bool isRuler = death.PreviousStatus == OfficerStatus.Ruler || before.World.Forces.Any(f => f.LeaderId == death.OfficerId);
                int? captor = death.PreviousSettlementId is int prison ? before.World.Settlements.Single(s => s.Id == prison).OwnerId :
                    death.PreviousUnitId is int detention ? before.World.Units.FirstOrDefault(u => u.Id == detention)?.OwnerId : null;
                // A duel scene already owns its death card; its presented-fact index suppresses this path.
                if (isRuler) Message("ruler-death");
                else if (death.PreviousStatus == OfficerStatus.Captive && captor is int captorId)
                { Force("captor", captorId); Message("captive-death"); }
                else if (death.PreviousForceId is int owner)
                {
                    Force("force", owner);
                    int? corps = death.PreviousSettlementId is int home ? before.World.Settlements.Single(s => s.Id == home).CorpsId :
                        death.PreviousUnitId is int unit ? before.World.Units.FirstOrDefault(u => u.Id == unit)?.CorpsId : null;
                    values["corps"] = corps is int direct ? MessageBinding.Reference(MessageEntityKind.Corps, direct) : ProgressMessageProducer.Force(owner);
                    Message("officer-death");
                }
                else if ((death.PreviousSettlementId ?? death.PreviousRegionId) is int home)
                { Place(home); Message("free-officer-death"); }
                break;
            default: return false;
        }
        return true;
    }
}
