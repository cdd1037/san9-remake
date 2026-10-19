using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Creates one retained capture result from occurrence identities, before the next battle fact.</summary>
public static class CapturePresentation
{
    public static IReadOnlyList<CommandMessage> Read(StrategySnapshot before, WorldMessageContext context, TextCatalog texts,
        CapturePresentationDefinition content, SettlementCapturedEvent fact)
    {
        if (fact.Destroyed || fact.Source.ForceId is not int winner) return [];
        bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
        bool won = Player(winner);
        if (!won && !Player(fact.PreviousOwnerId)) return [];
        var force = before.World.Forces.Single(f => f.Id == winner);
        var values = new Dictionary<string, MessageBinding>
        {
            ["place"] = ProgressMessageProducer.Place(fact.SettlementId),
            ["force"] = ProgressMessageProducer.Force(winner)
        };
        if (force.LeaderId is int ruler) values["ruler"] = ProgressMessageProducer.Person(ruler);
        if ((fact.Source.CommanderId ?? force.LeaderId) is int actor) values["actor"] = ProgressMessageProducer.Person(actor);
        if (fact.PreviousOwnerId is int previous) values["previous-force"] = ProgressMessageProducer.Force(previous);
        var place = before.World.Settlements.Single(s => s.Id == fact.SettlementId);
        int type = before.MilitaryRules!.Place(place).FacilityTypeId!.Value;
        var presentation = content.Places[type];
        var message = ProgressMessageProducer.Produce(content.Messages[won ? "won" : "lost"],
            context.AtOccurrence(fact.Clock), texts, values,
            _ => throw new InvalidDataException("Capture results must not require randomness."));
        bool unowned = fact.PreviousOwnerId is null;
        return [message with
        {
            IllustrationRole = unowned ? null : presentation.Illustration,
            BackgroundMusicRole = !unowned && presentation.UsesMusic ? won ? content.VictoryMusic : content.LossMusic : null,
            BackgroundMusicLoop = false,
            SoundRole = unowned || !presentation.UsesMusic ? content.CaptureSound : null
        }];
    }
}
