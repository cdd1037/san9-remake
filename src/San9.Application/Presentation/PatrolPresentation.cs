using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record CommandMessage(ProducedMessage Message, int? SpeakerId, string? SoundRole);

/// <summary>Creates a retained message sequence once, from a committed command and its pre-command snapshot.</summary>
public static class PatrolPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, WorldMessageContext context,
        PatrolPresentationDefinition content, DevelopmentCompletedEvent result, Func<int, int> random)
    {
        if (result.Command.Activity != DomesticActivity.Patrol) throw new ArgumentException("Not a patrol result.", nameof(result));
        var command = result.Command;
        var home = before.World.Settlements.Single(s => s.Id == command.SettlementId);
        var force = before.World.Forces.Single(f => f.Id == command.ForceId);
        var abilities = before.Officers.ToDictionary(o => o.Id, o => o.Abilities);
        var participants = DomesticDevelopment.OrderParticipants(before.World.Officers.Where(p => command.OfficerIds.Contains(p.Id)), abilities, DomesticActivity.Patrol);
        int first = participants[0].Id, ruler = force.LeaderId ?? first;
        List<CommandMessage> sequence = [];
        MessageBinding Person(int id) => MessageBinding.Reference(MessageEntityKind.Person, id);
        void Dialogue(string key, int speaker, int recipient, string? sound = null) => sequence.Add(new(
            context.Produce(key, new Dictionary<string, MessageBinding> { ["person.0"] = Person(speaker),
                ["person.1"] = Person(recipient), ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement, home.Id) }, random), speaker, sound));
        switch (result.Bonus)
        {
            case { Kind: DomesticBonusKind.SpecialPair, PartnerId: int second } bonus:
                Dialogue(content.Messages["pair-first"], bonus.SpeakerId, second);
                Dialogue(content.Messages["pair-second"], second, bonus.SpeakerId);
                break;
            case { Kind: DomesticBonusKind.Affinity } bonus:
                Dialogue(content.Messages["cooperative"], bonus.SpeakerId, ruler);
                Dialogue(content.Messages["citizen"], content.CitizenId, bonus.SpeakerId, "patrol-citizen");
                break;
            case null:
                Dialogue(content.Messages["normal"], first, ruler);
                break;
            default: throw new InvalidDataException("Incomplete domestic bonus result.");
        }
        if (result.Increment > 0)
        {
            int order = home.PublicOrder + result.Increment;
            long oldGrowth = PatrolDraft.ReadDraftGrowth(before.World, home);
            long growth = PatrolDraft.ReadDraftGrowth(before.World, home with { PublicOrder = order });
            sequence.Add(new(context.Produce(content.Messages["result"], new Dictionary<string, MessageBinding>
            {
                ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement, home.Id),
                ["number.0"] = new(order), ["number.1"] = new(result.Increment),
                ["number.2"] = new(growth), ["number.3"] = new(growth - oldGrowth)
            }, random), null, "domestic-result"));
        }
        foreach (var advice in content.Advice)
        {
            if (advice.RequiresBarbarianThreat && (force.IsBarbarian || !before.World.Forces.Any(f => f.IsBarbarian && !f.IsDormant && !f.IsEliminated))) continue;
            if (advice.Cities.Length > 0)
            {
                var cities = before.World.Settlements.Where(s => advice.Cities.Contains(s.Id) && !s.IsDormant && !s.IsDestroyed).ToArray();
                if (!cities.Any(s => s.OwnerId == force.Id) || cities.Sum(s => (long)(advice.Metric switch
                    { AdviceMetric.PublicOrder => s.PublicOrder, AdviceMetric.Commerce => s.Commerce, _ => s.Durability })) >= advice.Threshold) continue;
            }
            if (random(100) >= 10) continue;
            Dialogue(advice.MessageKey, advice.SpeakerId, first);
            break;
        }
        return sequence.AsReadOnly();
    }
}
