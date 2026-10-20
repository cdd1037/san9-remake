using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal static class ProgressMessageProducer
{
    internal static MessageBinding Person(int id) => MessageBinding.Reference(MessageEntityKind.Person, id);
    internal static MessageBinding Force(int id) => MessageBinding.Reference(MessageEntityKind.Force, id);
    internal static MessageBinding Place(int id) => MessageBinding.Reference(MessageEntityKind.Settlement, id);
    internal static CommandMessage Produce(DecisionMessageDefinition definition, WorldMessageContext context,
        TextCatalog texts, IReadOnlyDictionary<string, MessageBinding> values, Func<int, int> random)
    {
        var inputs = MessageGraph.Requirements(definition.Key, texts.Messages).Bindings
            .ToDictionary(slot => slot, slot => values[definition.Bindings[slot]]);
        return new(context.Produce(definition.Key, inputs, random),
            definition.Speaker is { } role ? values[role].Entity!.Id : null, definition.Sound);
    }
}

/// <summary>Public outcomes in commit order. Low-level resource and movement events are not reports.</summary>
public static class ProgressMessages
{
    public static IReadOnlyList<MapMessageRecord> Read(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, ProgressPresentationDefinition content,
        IReadOnlyList<DomainEvent> events, int? eventIndex = null)
    {
        List<MapMessageRecord> result = [];
        bool Player(int? force) => force is int id && before.World.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);
        MapCell? PlaceLocation(int id) => after.Settlements.Concat(before.Settlements).FirstOrDefault(s => s.Id == id) is { } place
            ? new(place.X, place.Y) : null;
        int? DetentionOwner(int? settlement, int? unit) => settlement is int place
            ? before.World.Settlements.Single(s => s.Id == place).OwnerId : unit is int field
                ? before.World.Units.Single(u => u.Id == field).OwnerId : null;
        MapCell? UnitLocation(int id, int occurrenceIndex)
        {
            // Replay only geography established before this fact, never a later termination location.
            for (int index = occurrenceIndex; index >= 0; index--)
            {
                if (events[index] is UnitMovedEvent moved && moved.UnitId == id)
                    return MapCoordinates.Cell(moved.To);
                if (events[index] is UnitDeployedEvent deployed && deployed.UnitId == id)
                    return PlaceLocation(deployed.SettlementId);
            }
            return before.World.Units.FirstOrDefault(u => u.Id == id)?.Cell;
        }
        MapCell? PersonLocation(int id) => before.World.Officers.SingleOrDefault(p => p.Id == id) is { } person
            ? person.SettlementId is int place ? PlaceLocation(place) : person.UnitId is int unit
                ? before.World.Units.FirstOrDefault(u => u.Id == unit)?.Cell : null : null;
        int end = eventIndex is int selected ? selected + 1 : events.Count;
        for (int index = eventIndex ?? 0; index < end; index++)
        {
            var fact = events[index];
            string? role = null;
            var importance = MapReportImportance.Summary;
            GameClock clock = after.World.Clock;
            MapCell? location = null;
            int? colorForce = null;
            List<BattleObjectContext> objects = [];
            Dictionary<string, MessageBinding> values = [];
            void Person(string name, int id) => values[name] = ProgressMessageProducer.Person(id);
            void Force(string name, int id) => values[name] = ProgressMessageProducer.Force(id);
            void Place(int id) { values["place"] = ProgressMessageProducer.Place(id); location = PlaceLocation(id); }
            switch (fact)
            {
                case BattleContactStartedEvent contact when Player(contact.Participant.ForceId):
                    var participant = contact.Participant;
                    if (!participant.IsSettlement && participant.CommanderId is null) break;
                    clock = contact.Clock; objects.Add(participant); objects.Add(contact.Opponent);
                    colorForce = participant.ForceId;
                    location = participant.Position is { } contactPosition ? MapCoordinates.Cell(contactPosition) : PlaceLocation(participant.Id);
                    if (participant.IsSettlement) { Place(participant.Id); role = "settlement-contact"; }
                    else { Person("actor", participant.CommanderId!.Value); role = "unit-contact"; }
                    break;
                case UnitWaypointReachedEvent waypoint:
                    clock = waypoint.Clock; colorForce = waypoint.Unit.ForceId;
                    location = MapCoordinates.Cell(waypoint.Position);
                    objects.Add(waypoint.Unit with { Position = waypoint.Position });
                    values["unit"] = MessageBinding.Reference(MessageEntityKind.Unit, waypoint.Unit.Id);
                    role = "waypoint-reached";
                    break;
                case UnitTerminatedEvent lost when lost.Reason != UnitTerminationReason.ForceEliminated && lost.Target is { } target &&
                    !(lost.Winner is null && events.OfType<ForceSurrenderedEvent>()
                        .Any(s => s.ForceId == target.ForceId && s.Clock == lost.Clock)):
                    clock = lost.Clock; objects.Add(target); location = target.Position is { } point ? MapCoordinates.Cell(point) : target.ServicePlaceId is int home ? PlaceLocation(home) : null;
                    colorForce = target.ForceId;
                    values["unit"] = MessageBinding.Reference(target.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, target.Id);
                    role = "unit-lost";
                    if (!Player(target.ForceId) && !Player(lost.Source?.ForceId))
                    {
                        if (target.ForceId is not int owner) { role = null; break; }
                        Force("enemy-force", owner); role = "unit-lost-other"; importance = MapReportImportance.Detailed;
                    }
                    else if (!Player(target.ForceId) && lost.Source is { } source && target.CommanderId is int commander)
                    {
                        objects.Add(source); values["unit"] = MessageBinding.Reference(source.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, source.Id);
                        Person("other", commander); Force("enemy-force", target.ForceId!.Value); role = "unit-defeated"; colorForce = source.ForceId;
                    }
                    break;
                case SettlementCapturedEvent captured when captured.Source is { ForceId: not null } capturingSource:
                    clock = captured.Clock; Place(captured.SettlementId); Force("force", capturingSource.ForceId!.Value);
                    colorForce = Player(capturingSource.ForceId) ? capturingSource.ForceId : captured.PreviousOwnerId;
                    role = "territory-changed";
                    if (!Player(capturingSource.ForceId) && !Player(captured.PreviousOwnerId))
                    {
                        importance = MapReportImportance.Detailed;
                        if (captured.Destroyed) role = "facility-destroyed-other";
                        else if (captured.PreviousOwnerId is int previousOwner)
                        { Force("enemy-force", previousOwner); role = "territory-changed-other"; }
                        else { role = null; break; }
                    }
                    else if (captured.Destroyed)
                    {
                        role = "facility-destroyed";
                        if (Player(capturingSource.ForceId) && capturingSource.CommanderId is int destroyer)
                        { Person("actor",destroyer); role = "facility-won"; }
                    }
                    else if (capturingSource.CommanderId is int capturingCommander)
                    { Person("actor", capturingCommander); role = Player(capturingSource.ForceId) ? "settlement-won" : "settlement-lost"; }
                    break;
                case FacilityDestroyedEvent destroyed:
                    clock = destroyed.Clock; Place(destroyed.SettlementId);
                    colorForce = Player(destroyed.Source.ForceId) ? destroyed.Source.ForceId : destroyed.PreviousOwnerId;
                    role = "facility-destroyed";
                    if (!Player(destroyed.Source.ForceId) && !Player(destroyed.PreviousOwnerId))
                    { role = "facility-destroyed-other"; importance = MapReportImportance.Detailed; }
                    else if (Player(destroyed.Source.ForceId) && destroyed.Source.CommanderId is int demolisher)
                    { Person("actor",demolisher); role = "facility-won"; }
                    break;
                case FacilityConstructionChangedEvent { Phase: "completed", Occurrence: { } construction } built when Player(construction.ForceId):
                    role = "construction-completed"; clock = built.Clock; Place(built.SettlementId);
                    colorForce = construction.ForceId;
                    break;
                case UnitAbsorbedEvent delivered when delivered.WasTransport && Player(delivered.Source.ForceId) && delivered.Source.CommanderId is int deliveringCommander:
                    role = "transport-completed"; clock = delivered.Clock; Place(delivered.SettlementId); Person("actor", deliveringCommander); colorForce = delivered.Source.ForceId;
                    break;
                case OfficerCapturedEvent captured when Player(captured.CaptorId) || Player(before.World.Officers.Single(p => p.Id == captured.OfficerId).OwnerId):
                    int? capturedOwner = before.World.Officers.Single(p => p.Id == captured.OfficerId).OwnerId;
                    role = Player(captured.CaptorId) ? "enemy-captured" : "person-captured"; clock = captured.Clock; Person("actor", captured.OfficerId); Force("force", captured.CaptorId);
                    colorForce = Player(capturedOwner) ? capturedOwner : captured.CaptorId;
                    location = captured.SettlementId is int prison ? PlaceLocation(prison) : captured.UnitId is int unit
                        ? UnitLocation(unit,index) : PersonLocation(captured.OfficerId);
                    break;
                case PrisonerEscapedEvent escaped when before.World.Officers.Single(p => p.Id == escaped.OfficerId) is { } person &&
                    (Player(person.OwnerId) || escaped.DetentionSettlementId is int detention && Player(before.World.Settlements.Single(s => s.Id == detention).OwnerId) ||
                        escaped.DetentionUnitId is int detentionUnit && Player(before.World.Units.Single(u => u.Id == detentionUnit).OwnerId)):
                    role = "prisoner-escaped"; clock = escaped.Clock; Person("actor", escaped.OfficerId);
                    Place(escaped.DetentionSettlementId ?? before.World.Units.Single(u => u.Id == escaped.DetentionUnitId).ServicePlaceId!.Value); colorForce = person.OwnerId;
                    break;
                case ForceLeaderChangedEvent succession when succession.PreviousId is int previous:
                    role = "ruler-succeeded"; clock = succession.Clock; Person("actor", succession.OfficerId); Person("other", previous);
                    colorForce = succession.ForceId; location = PersonLocation(previous);
                    break;
                case ForceEliminatedEvent eliminated:
                    role = "force-eliminated"; clock = eliminated.Clock; Force("force", eliminated.ForceId); colorForce = eliminated.ForceId;
                    location = before.World.Forces.Single(f => f.Id == eliminated.ForceId).LeaderId is int ruler ? PersonLocation(ruler) : null;
                    break;
                case OfficerDiedEvent death when (Player(death.PreviousForceId) || death.Cause == OfficerDeathCause.Natural &&
                    (before.World.Forces.Any(f => f.LeaderId == death.OfficerId) ||
                        death.PreviousStatus == OfficerStatus.Captive && Player(DetentionOwner(death.PreviousSettlementId,death.PreviousUnitId)))) && death.Cause is
                    OfficerDeathCause.Natural or OfficerDeathCause.Melee or OfficerDeathCause.Sniping or OfficerDeathCause.Duel:
                    role = death.Cause switch { OfficerDeathCause.Natural => "natural-death", OfficerDeathCause.Melee => "melee-death", OfficerDeathCause.Sniping => "sniping-death", _ => "duel-death" };
                    clock = death.Clock; Person("actor", death.OfficerId); colorForce = death.PreviousForceId;
                    if (death.Cause == OfficerDeathCause.Natural)
                    {
                        if (before.World.Forces.Any(f => f.LeaderId == death.OfficerId)) role = "ruler-death";
                        else if (death.PreviousStatus == OfficerStatus.Captive)
                        {
                            role = "captive-death";
                            int captor = DetentionOwner(death.PreviousSettlementId,death.PreviousUnitId)!.Value;
                            Force("force",captor);
                        }
                        else
                        {
                            int? corps = death.PreviousUnitId is int previousUnit ? before.World.Units.Single(u => u.Id == previousUnit).CorpsId
                                : death.PreviousSettlementId is int previousPlace ? before.World.Settlements.Single(s => s.Id == previousPlace).CorpsId : null;
                            values["corps"] = corps is int direct ? MessageBinding.Reference(MessageEntityKind.Corps,direct)
                                : ProgressMessageProducer.Force(death.PreviousForceId!.Value);
                            Force("force",death.PreviousForceId!.Value);
                        }
                    }
                    location = death.PreviousSettlementId is int place ? PlaceLocation(place) : death.PreviousUnitId is int deathUnit
                        ? UnitLocation(deathUnit,index) : PersonLocation(death.OfficerId);
                    break;
            }
            if (role is null) continue;
            var occurrence = (role == "construction-completed" ? context.ForWorld(after.World) : context).AtOccurrence(clock, objects.ToArray());
            string identity = $"report:{before.World.Revision}:{index}:{role}";
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision,$"report-text:{index}:{role}"));
            var message = ProgressMessageProducer.Produce(content.Records[role], occurrence, texts, values,n => (int)(random.NextUInt64() % (ulong)n));
            result.Add(new(identity, message.Message, location,
                MapRecordColors.Resolve(before, after, colorForce), importance));
            if (role is "settlement-won" or "settlement-lost")
            {
                string territoryRole = role == "settlement-won" ? "territory-changed" : "territory-lost";
                var territory = ProgressMessageProducer.Produce(content.Records[territoryRole],occurrence,texts,values,
                    n => (int)(random.NextUInt64() % (ulong)n));
                result.Add(new($"report:{before.World.Revision}:{index}:{territoryRole}",
                    territory.Message, location, result[^1].ForceColorCode));
            }
        }
        return result;
    }
}
