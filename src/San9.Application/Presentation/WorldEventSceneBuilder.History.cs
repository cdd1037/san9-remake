using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentHistory(DomainEvent fact)
    {
        switch (fact)
        {
            case EmperorDeathEvent emperorDeath:
                clock = emperorDeath.Clock;
                Message(content.People["emperor-first"] == emperorDeath.OfficerId ? "emperor-first-death" : "emperor-second-death"); break;
            case ChainedPlotEvent chained:
                clock = chained.Clock;
                switch (chained.Step)
                {
                    case ChainedPlotStep.Prelude: Message("chained-prelude", movie: "chained-prelude"); break;
                    case ChainedPlotStep.Coup:
                        Message("chained-coup", movie: "chained-coup"); Person("actor", chained.RulerId); Message("ruler-death");
                        Message("chained-result"); Person("actor", chained.SuccessorId); Person("other", chained.RulerId);
                        location = PersonLocation(chained.SuccessorId); colorForce = chained.ForceId;
                        Message("successor-declaration"); break;
                    case ChainedPlotStep.RulerUnavailable: Message("chained-ruler-unavailable"); break;
                    case ChainedPlotStep.ConspiratorsUnavailable: Message("chained-conspirators-unavailable"); break;
                    case ChainedPlotStep.CourtUnavailable:
                        var emperor = before.EmperorId;
                        if (emperor is int emperorId) { Person("emperor", emperorId); Message("chained-court-unavailable"); }
                        break;
                }
                break;
            case ThreeVisitsEvent visits:
                clock = visits.Clock; Person("actor", visits.RulerId); location = PersonLocation(visits.RulerId);
                switch (visits.Step)
                {
                    case ThreeVisitsStep.Recommendation:
                        Person("actor", content.People["recommender"]); location = PersonLocation(content.People["recommender"]);
                        Message("three-visits-recommendation"); break;
                    case ThreeVisitsStep.FirstVisit: Message("three-visits-first", movie: "three-visits-first"); break;
                    case ThreeVisitsStep.Recruitment:
                        Message("three-visits-recruitment", movie: "three-visits-recruitment"); Message("three-visits-result");
                        int force = before.World.Officers.Single(p => p.Id == visits.RulerId).OwnerId!.Value;
                        Force("force", force); Message("force-trust"); break;
                    case ThreeVisitsStep.Expired:
                        var visitingRuler = before.World.Officers.Single(p => p.Id == visits.RulerId);
                        if (visitingRuler.Status == OfficerStatus.Ruler && Player(visitingRuler.OwnerId)) Message("three-visits-expired-reaction");
                        Message("three-visits-expired"); break;
                }
                break;
            case RedCliffsEvent cliffs:
                clock = cliffs.Clock; Force("force", cliffs.ForceId); Force("previous-force", cliffs.OpposingForceId);
                Message("red-cliffs-introduction", movie: "red-cliffs"); Message("red-cliffs-result"); Message("red-cliffs-soldiers");
                Message("red-cliffs-trust"); Message("red-cliffs-enemy-trust");
                if (before.World.Officers.Count(p => p.OwnerId == cliffs.ForceId && p.IsServing) > 1) Message("red-cliffs-loyalty"); break;
            case GuanduEvent guandu:
                clock = guandu.Clock; Person("actor", guandu.RecruitedOfficerId); location = PersonLocation(guandu.RecruitingOfficerId);
                Message("guandu-introduction"); Message("guandu-advice", movie: "guandu"); Message("guandu-result");
                Message("guandu-soldiers"); Message("guandu-food"); Message("guandu-trust"); Message("guandu-enemy-trust");
                if (before.World.Officers.Count(p => p.OwnerId == guandu.ForceId && p.IsServing) > 1) Message("guandu-loyalty"); break;
            case QiaoMarriageEvent qiao:
                clock = qiao.Clock; Force("force", qiao.ForceId); Message("qiao-introduction", movie: "qiao-marriage");
                Message("qiao-result"); Message("force-trust"); if (qiao.RivalForceId is not null) Message("qiao-rival"); break;
            case SunMarriageEvent sun:
                clock = sun.Clock; Force("previous-force", sun.DonorForceId);
                if (Player(sun.RecipientForceId)) Message("sun-marriage-introduction", movie: "sun-marriage");
                Message("sun-marriage-result"); Message("sun-marriage-relation");
                if (sun.GrantedTactics && Learned([sun.BrideId, before.World.Forces.Single(f => f.Id == sun.RecipientForceId).LeaderId!.Value])) Message("sun-marriage-tactics"); break;
            case FiveTigersRecognizedEvent tigers:
                clock = tigers.Clock; Force("force", tigers.ForceId); Message("five-tigers-introduction", movie: "five-tigers");
                Message("five-tigers-result"); Message("force-trust"); Message("five-tigers-loyalty");
                if (Learned(tigers.OfficerIds)) Message("five-tigers-tactics"); break;
            case ExpeditionMemorialEvent memorial:
                clock = memorial.Clock; Force("force", memorial.ForceId);
                if (Player(memorial.ForceId)) Message("memorial-introduction", movie: "expedition-memorial");
                Message("memorial-result"); TrustAndLoyalty(memorial.ForceId);
                if (memorial.GrantedTactics && Learned(memorial.RewardedOfficerIds)) Message("memorial-tactics"); break;
            default: return false;
        }
        return true;
    }
}
