using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal static class MapNotificationSpeeches
{
    internal static IReadOnlyList<CommandMessage> Read(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, ProgressPresentationDefinition content,
        DomainEvent fact, int index)
    {
        BattleObjectContext participant;
        GameClock clock;
        string role;
        BattleObjectContext? opponent = null;
        switch (fact)
        {
            case UnitPolicyRespondedEvent policy:
                if (!before.World.Simulation.Controls.Any(c => c.Kind == ControllerKind.Player && c.ForceId == policy.Unit.ForceId)) return [];
                participant = policy.Unit; clock = policy.Clock; role = ProgressFeedback.PolicyRole(policy.Response);
                break;
            case UnitWaypointReachedEvent waypoint:
                participant = waypoint.Unit with { Position = waypoint.Position };
                clock = waypoint.Clock; role = "waypoint-reached";
                break;
            case BattleContactStartedEvent contact:
                participant = contact.Participant; opponent = contact.Opponent; clock = contact.Clock;
                if (!new[] { participant, opponent }.Any(o => before.World.Simulation.Controls
                    .Any(c => c.Kind == ControllerKind.Player && c.ForceId == o.ForceId))) return [];
                role = participant.IsSettlement ? "contact-building" : contact.Initiated ? "contact-attack" : "contact-defense";
                if (!participant.IsSettlement && contact.Soldiers >= 3000)
                {
                    if ((ulong)contact.Soldiers >= 2UL * contact.OpponentSoldiers) role += "-strong";
                    else if (2UL * contact.Soldiers < contact.OpponentSoldiers) role += "-weak";
                }
                break;
            default: return [];
        }
        if (participant.CommanderId is not int actor || opponent is { CommanderId: null }) return [];
        var definition = content.MapSpeeches[role];
        Dictionary<string, MessageBinding> values = new() { ["actor"] = ProgressMessageProducer.Person(actor) };
        if (fact is UnitPolicyRespondedEvent response)
        {
            if (response.OtherOfficerId is int otherOfficer) values["other"] = ProgressMessageProducer.Person(otherOfficer);
            if (response.HomeId is int home) values["place"] = ProgressMessageProducer.Place(home);
            if (response.Unit.ServicePlaceId is int origin) values["other-place"] = ProgressMessageProducer.Place(origin);
            if (response.PreviousPlaceId is int previousPlace) values["previous-place"] = ProgressMessageProducer.Place(previousPlace);
            if (MessageGraph.Requirements(definition.Key, texts.Messages).Bindings.Any(slot => !values.ContainsKey(definition.Bindings[slot]))) return [];
        }
        if (opponent?.CommanderId is int other) values["other"] = ProgressMessageProducer.Person(other);
        var objects = opponent is null ? new[] { participant } : new[] { participant, opponent };
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"map-notification:{index}:{role}"));
        var message = ProgressMessageProducer.Produce(definition, context.AtOccurrence(clock, objects), texts, values,
            n => (int)(random.NextUInt64() % (ulong)n));
        var place = participant.IsSettlement ? before.Settlements.Concat(after.Settlements).First(s => s.Id == participant.Id) : null;
        MapCell location = participant.Position is { } position ? MapCoordinates.Cell(position) : new(place!.X, place.Y);
        int? color = before.Forces.Concat(after.Forces).FirstOrDefault(f => f.Id == participant.ForceId)?.ColorCode;
        var speech = new MapSpeechMessage($"map-notification:{before.World.Revision}:{index}:{role}",
            message.Message, actor, texts.Resolve(before.MessageSpeaker(actor).NameKey), location,
            participant.IsSettlement || fact is UnitWaypointReachedEvent ? null : participant.Id, color);
        return [message with { MapSpeech = speech }];
    }
}
