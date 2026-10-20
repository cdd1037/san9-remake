using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Confirmation retains its source snapshot; opening or cancelling never submits a command.</summary>
public sealed class FacilityDemolitionDraft(GameSession session, int settlementId)
{
    public StrategySnapshot View { get; } = session.View;
    public int SettlementId { get; } = settlementId;
    public bool CanExecute => View.World.Revision == session.Snapshot.Revision &&
        UnavailableReasonKey(session.View, SettlementId) is null;

    public static string? UnavailableReasonKey(StrategySnapshot view, int id)
    {
        var facility = view.World.Settlements.SingleOrDefault(s => s.Id == id);
        if (facility is null || facility.Kind != San9.Core.Domain.SettlementKind.Facility ||
            view.MilitaryRules is not { } rules || !rules.TryPlace(facility, out var place) || !place.CanDemolish)
            return CommandMenuSkin.ReasonKey("unavailable");
        return new MapCommandAvailability(view).SettlementReasonKey(id, "demolish");
    }

    public ProducedMessage Confirmation(WorldMessageContext context, DemolitionPresentationDefinition definition) =>
        context.Produce(definition.Confirmation, new Dictionary<string, MessageBinding>
        {
            ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement, SettlementId)
        }, _ => throw new InvalidDataException("Demolition confirmation must not require randomness."));

    public DemolishFacilityCommand CreateCommand(Guid id, long sequence) => CanExecute
        ? new(id, View.RequirePlayerForce(), sequence, SettlementId)
        : throw new InvalidOperationException("Demolition confirmation is no longer valid.");
}
