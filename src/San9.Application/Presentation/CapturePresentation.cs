using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Explains the attack and any separate receipt before publishing the captured place.</summary>
public static class CapturePresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context, TextCatalog texts,
        CapturePresentationDefinition content, SettlementCapturedEvent fact)
    {
        if (fact.Destroyed || fact.Attacker.ForceId is not int attacker || fact.Source.ForceId is not int recipient) return [];
        bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
        if (!Player(attacker) && !Player(recipient) && !Player(fact.PreviousOwnerId)) return [];
        bool won = !Player(fact.PreviousOwnerId) || Player(attacker);
        var values = new Dictionary<string, MessageBinding>
        {
            ["place"] = ProgressMessageProducer.Place(fact.SettlementId)
        };
        void BindForce(int id)
        {
            values["force"] = ProgressMessageProducer.Force(id);
            values.Remove("ruler");
            if (before.World.Forces.Single(f => f.Id == id).LeaderId is int ruler)
                values["ruler"] = ProgressMessageProducer.Person(ruler);
        }
        BindForce(attacker);
        if (fact.Attacker.CommanderId is int actor) values["actor"] = ProgressMessageProducer.Person(actor);
        else if (values.TryGetValue("ruler", out var ruler)) values["actor"] = ruler;
        if (fact.PreviousOwnerId is int previous) values["previous-force"] = ProgressMessageProducer.Force(previous);
        var place = before.World.Settlements.Single(s => s.Id == fact.SettlementId);
        int type = before.MilitaryRules!.Place(place).FacilityTypeId!.Value;
        var presentation = content.Places[type];
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision,
            $"capture:{fact.SettlementId}:{attacker}:{recipient}:{fact.PreviousOwnerId}"));
        var message = ProgressMessageProducer.Produce(content.Messages[won ? "won" : "lost"],
            context.AtOccurrence(fact.Clock), texts, values,
            n => (int)(random.NextUInt64() % (ulong)n));
        bool unowned = fact.PreviousOwnerId is null;
        List<CommandMessage> result = [message with
        {
            IllustrationRole = unowned ? null : presentation.Illustration,
            BackgroundMusicRole = !unowned && presentation.UsesMusic ? won ? content.VictoryMusic : content.LossMusic : null,
            BackgroundMusicLoop = false,
            SoundRole = unowned || !presentation.UsesMusic ? content.CaptureSound : null
        }];
        if (recipient != attacker)
        {
            BindForce(recipient);
            result.Add(ProgressMessageProducer.Produce(content.Messages[won ? "won-transfer" : "lost-transfer"],
                context.AtOccurrence(fact.Clock), texts, values, n => (int)(random.NextUInt64() % (ulong)n)));
        }
        return result;
    }
}
