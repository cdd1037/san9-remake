using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentExploration(DomainEvent fact)
    {
        switch (fact)
        {
            case ForceActivatedBySearchEvent activation:
                clock = activation.Clock; Person("actor", activation.SearchingOfficerId); Force("force", activation.SearchingForceId);
                location = PersonLocation(activation.SearchingOfficerId);
                Message("activation-introduction", movie: "force-activation"); Message("activation-reaction", speech: true);
                Message("activation-result"); Message("force-trust"); break;
            case ImperialCeremonyResolvedEvent ceremony:
                clock = ceremony.Clock; Person("actor", ceremony.RulerId); Force("force", ceremony.ForceId);
                Message("ceremony-introduction", movie: "imperial-ceremony"); Message("ceremony-result"); TrustAndLoyalty(ceremony.ForceId);
                foreach (var growth in events.OfType<OfficerAbilityGrownEvent>().Where(e => e.OfficerId == ceremony.RulerId))
                {
                    values["amount"] = new(growth.Current - growth.Previous); values["current"] = new(growth.Current); Message("ceremony-" + growth.Ability);
                }
                break;
            case SageVisitResolvedEvent sage:
                clock = sage.Clock; Search(sage.SearchingOfficerId, sage.SageOfficerId, sage.PlaceId, sage.ForceId); Visit(); Message("sage-loyalty"); break;
            case DivinerVisitResolvedEvent diviner:
                clock = diviner.Clock; Search(diviner.SearchingOfficerId, diviner.DivinerOfficerId, diviner.PlaceId, diviner.ForceId); Visit(); Message("diviner-advice", speech: true); break;
            case PhysicianVisitResolvedEvent physician:
                clock = physician.Clock; Search(physician.SearchingOfficerId, physician.PhysicianOfficerId, physician.PlaceId, physician.ForceId); Visit();
                Message("physician-book", speech: true); values["treasure-name"] = new(0, texts.Resolve(before.Information.Treasures.Single(t => t.Id == physician.TreasureId).NameKey));
                Message("treasure-acquired"); break;
            case WomenVisitResolvedEvent women:
                clock = women.Clock; Search(women.SearchingOfficerId, women.VisitorOfficerId, women.PlaceId, women.ForceId); Visit(); Message("women-teaching", speech: true);
                values["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, women.LearnedTacticId); Message("tactic-learned"); break;
            case SearchHealingResolvedEvent healing:
                clock = healing.Clock; int healingForce = before.World.Officers.Single(p => p.Id == healing.SearchingOfficerId).OwnerId!.Value;
                Search(healing.SearchingOfficerId, healing.HealerOfficerId, healing.PlaceId, healingForce);
                Message("healing-arrival"); Message("healing-greeting", speech: true); Message("healing-treatment"); Message("healing-departure", speech: true); Message("healing-result"); break;
            default: return false;
        }
        return true;
    }
}
