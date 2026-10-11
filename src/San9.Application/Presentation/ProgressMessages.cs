using System.Security.Cryptography;
using System.Text;
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
    internal static Guid Identity(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
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
                case UnitTerminatedEvent lost when lost.Target is { } target &&
                    (Player(target.ForceId) || Player(lost.Source?.ForceId)) &&
                    !(lost.Winner is null && events.OfType<ForceSurrenderedEvent>()
                        .Any(s => s.ForceId == target.ForceId && s.Clock == lost.Clock)):
                    clock = lost.Clock; objects.Add(target); location = target.Position is { } point ? MapCoordinates.Cell(point) : target.ServicePlaceId is int home ? PlaceLocation(home) : null;
                    colorForce = target.ForceId;
                    values["unit"] = MessageBinding.Reference(target.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, target.Id);
                    role = "unit-lost";
                    if (!Player(target.ForceId) && lost.Source is { } source && target.CommanderId is int commander)
                    {
                        objects.Add(source); values["unit"] = MessageBinding.Reference(source.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, source.Id);
                        Person("other", commander); Force("enemy-force", target.ForceId); role = "unit-defeated";
                    }
                    break;
                case SettlementCapturedEvent captured when captured.Source is { } capturingSource &&
                    (Player(capturingSource.ForceId) || Player(captured.PreviousOwnerId)):
                    clock = captured.Clock; Place(captured.SettlementId); Force("force", capturingSource.ForceId); colorForce = capturingSource.ForceId;
                    role = "territory-changed";
                    if (captured.Destroyed)
                    {
                        role = "facility-destroyed";
                        if (Player(capturingSource.ForceId) && capturingSource.CommanderId is int destroyer)
                        { Person("actor",destroyer); role = "facility-won"; }
                    }
                    else if (capturingSource.CommanderId is int capturingCommander)
                    { Person("actor", capturingCommander); role = Player(capturingSource.ForceId) ? "settlement-won" : "settlement-lost"; }
                    break;
                case FacilityDestroyedEvent destroyed when Player(destroyed.Source.ForceId) ||
                    Player(before.World.Settlements.Single(s => s.Id == destroyed.SettlementId).OwnerId):
                    clock = destroyed.Clock; Place(destroyed.SettlementId); colorForce = destroyed.Source.ForceId;
                    role = "facility-destroyed";
                    if (Player(destroyed.Source.ForceId) && destroyed.Source.CommanderId is int demolisher)
                    { Person("actor",demolisher); role = "facility-won"; }
                    break;
                case FacilityConstructionChangedEvent built when built.Phase == "completed" &&
                    Player(before.World.Settlements.FirstOrDefault(s => s.Id == built.SettlementId)?.OwnerId ??
                        after.World.Settlements.Single(s => s.Id == built.SettlementId).OwnerId):
                    role = "construction-completed"; clock = built.Clock; Place(built.SettlementId);
                    colorForce = before.World.Settlements.FirstOrDefault(s => s.Id == built.SettlementId)?.OwnerId ??
                        after.World.Settlements.Single(s => s.Id == built.SettlementId).OwnerId;
                    break;
                case UnitAbsorbedEvent delivered when delivered.WasTransport && Player(delivered.Source.ForceId) && delivered.Source.CommanderId is int deliveringCommander:
                    role = "transport-completed"; clock = delivered.Clock; Place(delivered.SettlementId); Person("actor", deliveringCommander); colorForce = delivered.Source.ForceId;
                    break;
                case OfficerCapturedEvent captured when Player(captured.CaptorId) || Player(before.World.Officers.Single(p => p.Id == captured.OfficerId).OwnerId):
                    role = Player(captured.CaptorId) ? "enemy-captured" : "person-captured"; clock = captured.Clock; Person("actor", captured.OfficerId); Force("force", captured.CaptorId); colorForce = captured.CaptorId;
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
            Guid identity = ProgressMessageProducer.Identity($"report:{before.World.Revision}:{index}:{role}");
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision,$"report-text:{index}:{role}"));
            var message = ProgressMessageProducer.Produce(content.Records[role], occurrence, texts, values,n => (int)(random.NextUInt64() % (ulong)n));
            result.Add(new(identity, message.Message, location,
                colorForce is int force ? before.Forces.Concat(after.Forces).First(f => f.Id == force).ColorCode : null));
            if (role is "settlement-won" or "settlement-lost")
            {
                string territoryRole = role == "settlement-won" ? "territory-changed" : "territory-lost";
                var territory = ProgressMessageProducer.Produce(content.Records[territoryRole],occurrence,texts,values,
                    n => (int)(random.NextUInt64() % (ulong)n));
                result.Add(new(ProgressMessageProducer.Identity($"report:{before.World.Revision}:{index}:{territoryRole}"),
                    territory.Message, location, result[^1].ForceColorCode));
            }
        }
        return result;
    }
}
