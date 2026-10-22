using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed partial class WorldEventSceneBuilder
{
    private readonly StrategySnapshot before, after;
    private readonly WorldMessageContext context;
    private readonly TextCatalog texts;
    private readonly EventPresentationDefinition content;
    private readonly IReadOnlyList<DomainEvent> events;
    private readonly int index;

    internal WorldEventSceneBuilder(StrategySnapshot before, StrategySnapshot after, WorldMessageContext context,
        TextCatalog texts, EventPresentationDefinition content, IReadOnlyList<DomainEvent> events, int index)
    {
        this.before = before; this.after = after; this.context = context;
        this.texts = texts; this.content = content; this.events = events; this.index = index;
        clock = after.World.Clock; occurrence = context; speakerContext = context;
        random = new(StableRandom.DeriveSeed((ulong)before.World.Revision, $"world-event:{index}"));
    }

    private readonly List<CommandMessage> messages = [];
    private readonly Dictionary<string, MessageBinding> values = [];
    private readonly List<MapMessageRecord> records = [];
    private GameClock clock;
    private MapCell? location;
    private int? colorForce;
    private WorldMessageContext occurrence;
    private WorldMessageContext speakerContext;
    private readonly StableRandom random;

    internal ExecutionOutput Build()
    {
        var fact = events[index];
        if (!(PresentForces(fact) || PresentHistory(fact) || PresentCourt(fact) ||
            PresentExploration(fact) || PresentAlliances(fact) || PresentFoodSupply(fact))) return new([], []);
        return new(messages, records);
    }

    private int Pick(int count) => (int)(random.NextUInt64() % (ulong)count);
    private bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
    private void Person(string role, int id) => values[role] = ProgressMessageProducer.Person(id);
    private void Force(string role, int id)
    { values[role] = ProgressMessageProducer.Force(id); if (role == "force") colorForce = id; }
    private void Place(int id) => values["place"] = MessageBinding.Reference(before.Settlements.Concat(after.Settlements).Any(s => s.Id == id)
        ? MessageEntityKind.Settlement : MessageEntityKind.Region, id);
    private MapCell? PlaceLocation(int id) => before.PlaceCell(id) ?? after.PlaceCell(id);
    private MapCell? PersonLocation(int id)
    {
        var person = before.World.Officers.Single(p => p.Id == id);
        if (person.SettlementId is int home) return PlaceLocation(home);
        if (person.RegionId is int region) return PlaceLocation(region);
        if (person.UnitId is not int unit) return null;
        for (int i = index; i >= 0; i--)
            if (events[i] is UnitMovedEvent moved && moved.UnitId == unit) return MapCoordinates.Cell(moved.To);
        return before.World.Units.SingleOrDefault(u => u.Id == unit)?.Cell;
    }
    private void Message(string role, bool focus = false, string? music = null, string? movie = null, bool record = false, bool musicLoop = false)
    {
        var definition = content.Messages[role];
        var message = ProgressMessageProducer.Produce(definition, occurrence.AtOccurrence(clock), texts, values, Pick)
            with
        {
            IllustrationRole = definition.Illustration == "actor" ? null : definition.Illustration,
            IllustrationPersonId = definition.Illustration == "actor" ? values["actor"].Entity!.Id : null,
            IllustrationDeceased = definition.Illustration == "actor",
            FocusLocation = focus ? location : null,
            BackgroundMusicRole = music,
            BackgroundMusicLoop = musicLoop,
            MovieRole = movie
        };
        if (message.SpeakerId is int speakerId)
            message = message with { Speaker = speakerContext.Speaker(speakerId) };
        messages.Add(message);
        if (record) AddRecord(message.Message);
    }
    private void AddRecord(ProducedMessage message, MapReportImportance importance = MapReportImportance.Summary) =>
        records.Add(new($"world-event:{before.World.Revision}:{index}:record:{records.Count}", message, location,
            MapRecordColors.Resolve(before, after, colorForce), importance));

    private void Record(string role, MapReportImportance importance = MapReportImportance.Summary) =>
        AddRecord(ProgressMessageProducer.Produce(content.Messages[role], occurrence.AtOccurrence(clock), texts, values, Pick).Message, importance);
    private void Founded(int force, int founder, int place, int? predecessor)
    {
        Person("actor", founder); Place(place); colorForce = force; location = PlaceLocation(place);
        // Only introduce this creation. Later succession and allegiance changes belong to later facts.
        var created = after.World.Forces.Single(f => f.Id == force) with { LeaderId = founder };
        occurrence = context.ForWorld(before.World with
        {
            Forces = before.World.Forces.Append(created).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.Id == founder
                ? p with { OwnerId = force, Status = OfficerStatus.Ruler } : p).ToImmutableArray()
        });
        speakerContext = occurrence;
        Message("founder-declaration", focus: true);
        if (predecessor is int previous) Force("previous-force", previous);
        Message(predecessor is null ? "force-founded" : "city-independent");
    }
    private void Search(int actor, int other, int place, int force)
    { Person("actor", actor); Person("other", other); Place(place); Force("force", force); location = PlaceLocation(place); colorForce = force; }
    private void Visit()
    { Message("visit-arrival"); Message("visit-greeting"); Message("visit-conversation"); Message("visit-departure"); }
    private void TrustAndLoyalty(int force, bool loyalty = true)
    {
        Force("force", force); Message("force-trust");
        if (loyalty && before.World.Officers.Count(p => p.OwnerId == force && p.IsServing) > 1) Message("force-loyalty");
    }
    private bool Learned(IEnumerable<int> people) => events.OfType<TacticLearnedEvent>().Any(e => people.Contains(e.OfficerId));
    private bool CompositeDeath(int id) => events.OfType<ChainedPlotEvent>().Any(e => e.Step == ChainedPlotStep.Coup && e.KilledOfficerIds.Contains(id));
}
