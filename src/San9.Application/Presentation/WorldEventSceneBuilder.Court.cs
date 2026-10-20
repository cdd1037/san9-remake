using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private bool PresentCourt(DomainEvent fact)
    {
        switch (fact)
        {
            case PeerageChangedEvent { Kind: PeerageChangeKind.HanzhongKing } hanzhong:
                clock = hanzhong.Clock; Force("force", hanzhong.ForceId);
                if (Player(hanzhong.ForceId)) Message("hanzhong-introduction", movie: "hanzhong");
                Message("hanzhong-result"); TrustAndLoyalty(hanzhong.ForceId); break;
            case PeerageChangedEvent rank when rank.Kind is PeerageChangeKind.Investiture or PeerageChangeKind.Promotion or PeerageChangeKind.Abdication or PeerageChangeKind.Proclamation:
                clock = rank.Clock; Force("force", rank.ForceId);
                int promoted = before.World.Forces.Single(f => f.Id == rank.ForceId).LeaderId!.Value;
                Person("actor", promoted); location = PersonLocation(promoted); colorForce = rank.ForceId;
                if (before.EmperorId is int reigning) Person("emperor", reigning);
                Person("messenger", content.People["messenger"]);
                // Each result needs this transition's rank, even when another promotion follows in the same publication.
                occurrence = context.ForWorld(before.World with
                {
                    Forces = before.World.Forces.Select(f => f.Id == rank.ForceId
                    ? f with { RankId = rank.RankId, CountryTitleId = rank.CountryTitleId } : f).ToImmutableArray()
                });
                bool playerRank = Player(rank.ForceId);
                if (rank.Kind == PeerageChangeKind.Abdication)
                {
                    if (playerRank) { Message("abdication-introduction"); Message("abdication-emperor", music: "event-court-music", musicLoop: true); Message("abdication-reply", music: "event-court-music", musicLoop: true); }
                    Message("abdication-result");
                }
                else if (rank.Kind == PeerageChangeKind.Proclamation)
                {
                    if (playerRank) { Message("proclamation-introduction"); Message("proclamation-declaration", speech: true); }
                    Message("proclamation-result");
                }
                else if (rank.Kind == PeerageChangeKind.Investiture)
                {
                    if (playerRank) { Message("investiture-introduction"); Message("investiture-decree", music: "event-court-music", musicLoop: true); Message("investiture-reply", music: "event-court-music", musicLoop: true); }
                    Message("investiture-result");
                }
                else
                {
                    if (playerRank) { Message("promotion-introduction"); Message("promotion-declaration", speech: true); }
                    Message("promotion-result");
                }
                TrustAndLoyalty(rank.ForceId); break;
            case EmperorTransferResolvedEvent transfer when !transfer.Invalidated && before.World.Simulation.PendingEvent is not EmperorTransferEventSession:
                clock = transfer.Clock; Force("force", transfer.ForceId); Person("emperor", transfer.EmperorId);
                values["dynasty-years"] = new(400); location = PlaceLocation(transfer.CityId);
                if (transfer.Choice == EmperorTransferChoice.Support)
                { Message("emperor-supported-dialogue"); Message("emperor-supported"); Message("force-trust"); }
                else { Message("emperor-deposed-dialogue"); Message("emperor-deposed"); }
                break;
            default: return false;
        }
        return true;
    }
}
