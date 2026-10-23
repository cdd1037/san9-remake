using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal static class PersonnelJourneyPresentation
{
    internal static ExecutionOutput Read(StrategySnapshot before, WorldMessageContext context, TextCatalog texts,
        PersonnelPresentationDefinition content, IReadOnlyList<DomainEvent> events, int index, Func<int, int> random)
    {
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        Dictionary<string, MessageBinding> values = [];
        bool Direct(int force, int? corps) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player) &&
            !before.World.Corps.Any(c => c.Id == corps && c.ControlMode == CorpsControlMode.Automatic);
        CommandMessage Produce(string role) => ProgressMessageProducer.Produce(content.Messages[role], context, texts, values, random);
        void Record(string role, int force, int home, string? detail = null)
        {
            var message = Produce(role).Message;
            if (detail is not null) message = message with { BbCode = message.BbCode + "\n" + detail };
            records.Add(new($"personnel-journey:{before.World.Revision}:{index}", message, before.PlaceCell(home),
                MapRecordColors.Resolve(before, before, force)));
        }
        if (events[index] is ReturnCargoDeliveredEvent delivered)
        {
            if (!Direct(delivered.ForceId, delivered.CorpsId)) return new([], []);
            context = context.AtOccurrence(delivered.Clock);
            values["actor"] = ProgressMessageProducer.Person(delivered.OfficerId);
            values["amount"] = new(delivered.Received);
            string role = delivered.Resource == ResourceKind.Money ? "cargo-money" : "cargo-food";
            messages.Add(Produce(role));
            string? overflow = delivered.Received == delivered.Amount ? null : CommandText.Resolve(texts, "result.return-cargo-overflow",
                CommandText.Resolve(texts, delivered.Resource == ResourceKind.Money ? "label.money" : "label.food"), delivered.Amount - delivered.Received);
            Record(role + "-record", delivered.ForceId, delivered.DestinationId, overflow);
        }
        else if (events[index] is ReturnCargoLostEvent lost)
        {
            if (!before.World.Simulation.Controls.Any(c => c.ForceId == lost.ForceId && c.Kind == ControllerKind.Player)) return new([], []);
            string text = CommandText.Resolve(texts, "result.return-cargo-lost", texts.Resolve(before.MessageSpeaker(lost.OfficerId).NameKey),
                CommandText.Resolve(texts, lost.Resource == ResourceKind.Money ? "label.money" : "label.food"), lost.Amount);
            records.Add(new($"return-cargo-lost:{before.World.Revision}:{index}", new("return-cargo-lost", lost.Clock, text), null,
                MapRecordColors.Resolve(before, before, lost.ForceId)));
        }
        else if (events[index] is PersonnelJourneyInterruptedEvent interrupted)
        {
            if (!Direct(interrupted.ForceId, interrupted.CorpsId)) return new([], []);
            context = context.AtOccurrence(interrupted.Clock);
            values["actor"] = ProgressMessageProducer.Person(interrupted.OfficerId);
            values["home"] = ProgressMessageProducer.Place(interrupted.DestinationId);
            values["command-name"] = new(0, OfficerCommands.JourneyOrder(interrupted.MissionKind, interrupted.Strategy));
            messages.Add(Produce("journey-interrupted"));
            messages.Add(Produce("journey-withdrawal"));
            Record("journey-interrupted-record", interrupted.ForceId, interrupted.DestinationId);
        }
        else if (Arrival(events[index]) is { } arrival && arrival.Occurrence is { Notify: true, RulerId: int ruler } occurrence &&
            (Direct(occurrence.ForceId, occurrence.SourceCorpsId) ||
                Direct(occurrence.ForceId, before.World.Settlements.Single(s => s.Id == arrival.Home).CorpsId)))
        {
            // A multi-person move is represented by independent tasks. Announce the group after its last arrival fact.
            var group = events.Select((e, i) => (Value: Arrival(e), Index: i)).Where(e => e.Value is { } a &&
                a.Clock == arrival.Clock && a.Home == arrival.Home && a.Returning == arrival.Returning &&
                a.Occurrence.Notify && a.Occurrence.ForceId == occurrence.ForceId && a.Occurrence.Sequence == occurrence.Sequence).ToArray();
            if (group[^1].Index != index) return new([], []);
            var people = group.Select(e => e.Value!.OfficerId).Distinct().ToList();
            if (people.Remove(ruler)) people.Insert(0, ruler);
            context = context.AtOccurrence(arrival.Clock);
            values["ruler"] = ProgressMessageProducer.Person(ruler);
            values["actor"] = ProgressMessageProducer.Person(people[0]);
            values["second"] = ProgressMessageProducer.Person(people.Count > 1 ? people[1] : people[0]);
            values["third"] = ProgressMessageProducer.Person(people.Count > 2 ? people[2] : people[0]);
            values["home"] = ProgressMessageProducer.Place(arrival.Home);
            values["count"] = new(people.Count);
            values["remaining"] = new(Math.Max(0, people.Count - 2));
            values["return-kind"] = MessageBinding.Return(arrival.Returning ? MessageReturnKind.Personnel : MessageReturnKind.Ordered);
            messages.Add(Produce("journey-arrived"));
            Record("journey-arrived-record", occurrence.ForceId, arrival.Home);
        }
        return new(messages, records);
    }

    private sealed record ArrivalFact(GameClock Clock, int OfficerId, int Home, bool Returning, PersonnelArrivalOccurrence Occurrence);
    private static ArrivalFact? Arrival(DomainEvent fact) => fact switch
    {
        OfficerMovedEvent e => new(e.Clock, e.OfficerId, e.DestinationId, false, e.Arrival),
        PersonnelReturnedEvent e => new(e.Clock, e.OfficerId, e.DestinationId, true, e.Arrival),
        _ => null
    };
}
