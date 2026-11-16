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
            case CountryTitleOfferedEvent offered when Player(offered.ForceId):
                clock = offered.Clock;
                PrepareCourt(offered.ForceId);
                ProclamationIntroduction(before.ImperialCourtEnded ? PeerageChangeKind.Proclamation : PeerageChangeKind.Abdication);
                break;
            case PeerageChangedEvent { Kind: PeerageChangeKind.HanzhongKing } hanzhong:
                clock = hanzhong.Clock; Force("force", hanzhong.ForceId);
                if (Player(hanzhong.ForceId)) Message("hanzhong-introduction", movie: "hanzhong");
                Message("hanzhong-result"); TrustAndLoyalty(hanzhong.ForceId); break;
            case PeerageChangedEvent rank when rank.Kind is PeerageChangeKind.Investiture or PeerageChangeKind.Promotion or PeerageChangeKind.Abdication or PeerageChangeKind.Proclamation:
                clock = rank.Clock;
                PrepareCourt(rank.ForceId);
                colorForce = rank.ForceId;
                // Each result needs this transition's rank, even when another promotion follows in the same publication.
                occurrence = context.ForWorld(before.World with
                {
                    Forces = before.World.Forces.Select(f => f.Id == rank.ForceId
                    ? f with { RankId = rank.RankId, CountryTitleId = rank.CountryTitleId } : f).ToImmutableArray()
                });
                bool playerRank = Player(rank.ForceId);
                if (rank.Kind is PeerageChangeKind.Abdication or PeerageChangeKind.Proclamation)
                {
                    if (playerRank && !AwaitingTitle(rank)) ProclamationIntroduction(rank.Kind);
                    Message(rank.Kind == PeerageChangeKind.Abdication ? "abdication-result" : "proclamation-result");
                }
                else if (rank.Kind == PeerageChangeKind.Investiture)
                {
                    if (playerRank) { Message("investiture-introduction"); Message("investiture-decree", music: "event-court-music", musicLoop: true); Message("investiture-reply", music: "event-court-music", musicLoop: true); }
                    Message("investiture-result");
                }
                else
                {
                    if (playerRank) { Message("promotion-introduction"); Message("promotion-declaration"); }
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

    private bool AwaitingTitle(PeerageChangedEvent rank) =>
        before.World.Simulation.PendingEvent is CountryTitleEventSession pending &&
        pending.ForceId == rank.ForceId && pending.ChangeKind == rank.Kind;

    private void PrepareCourt(int forceId)
    {
        Force("force", forceId);
        int ruler = before.World.Forces.Single(f => f.Id == forceId).LeaderId!.Value;
        Person("actor", ruler); location = PersonLocation(ruler);
        if (before.EmperorId is int emperor) Person("emperor", emperor);
        Person("messenger", content.People["messenger"]);
    }

    private void ProclamationIntroduction(PeerageChangeKind kind)
    {
        if (kind == PeerageChangeKind.Abdication)
        {
            Message("abdication-introduction");
            Message("abdication-emperor", music: "event-court-music", musicLoop: true);
            Message("abdication-reply", music: "event-court-music", musicLoop: true);
        }
        else
        {
            Message("proclamation-introduction");
            Message("proclamation-declaration");
        }
    }
}
