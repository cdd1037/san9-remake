using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal static class DiplomacyNegotiationMessages
{
    internal static string Quote(string role, CompensationOffer offer) => role + "-quote-" + (offer.Kind switch
    {
        CompensationKind.Free => "free", CompensationKind.Prisoner => "prisoner", CompensationKind.Money => "money",
        CompensationKind.Treasure => "treasure", CompensationKind.Refuse => "refuse",
        _ => throw new InvalidDataException("Unknown diplomatic compensation.")
    });

    internal static string Reply(string role, CompensationOffer offer, bool accepted) => role + "-" +
        (offer.Kind == CompensationKind.Free ? "thanks" : offer.Kind == CompensationKind.Refuse ? "refused" : accepted ? "accept" : "decline");

    internal static void BindOffer(Dictionary<string, MessageBinding> values, CompensationOffer offer)
    {
        values["cost"] = new(offer.Money);
        if (offer.PrisonerId is int prisoner) values["exchange-person"] = MessageBinding.Reference(MessageEntityKind.Person, prisoner);
        if (offer.TreasureId is int treasure) values["exchange-treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, treasure);
    }
}
