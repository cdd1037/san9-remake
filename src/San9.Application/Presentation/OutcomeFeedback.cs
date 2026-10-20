using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Projects occurrence facts into independent UI, speech and report channels.</summary>
internal static class OutcomeFeedback
{
    internal static ExecutionOutput Read(StrategySnapshot before, StrategySnapshot after, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content, IReadOnlyList<DomainEvent> events, int index, bool executionMap = true)
    {
        var fact = events[index];
        GameClock clock = fact switch
        {
            TacticLearnedEvent e => e.Clock, OfficerInjuryWorsenedEvent e => e.Clock, CommanderChangedEvent e => e.Clock,
            UnitCommanderChangedEvent e => e.Clock, CorpsLeaderChangedEvent e => e.Clock, CorpsDissolvedEvent e => e.Clock,
            HighSpiritsExpiredEvent e => e.Clock, OfficerAppearedEvent e => e.Clock, TreasuryCapturedEvent e => e.Clock,
            PrisonerEscapedEvent e => e.Clock, BattlePersonnelResolvedEvent e => e.Clock, _ => before.World.Clock
        };
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        Dictionary<string, MessageBinding> values = [];
        var occurrence = context.AtOccurrence(clock);
        MapCell? location = null;
        int? color = null;
        string identity = $"outcome:{before.World.Revision}:{index}";
        bool Player(int? force) => force is int id && before.World.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);
        void Person(string role, int id) => values[role] = ProgressMessageProducer.Person(id);
        void Force(string role, int id) => values[role] = ProgressMessageProducer.Force(id);
        void At(PersonOccurrence person)
        {
            color = person.ForceId;
            location = person.Position is { } position ? MapCoordinates.Cell(position) : person.PlaceId is int place
                ? before.PlaceCell(place) ?? after.PlaceCell(place) : null;
        }
        void Object(BattleObjectContext obj)
        {
            occurrence = context.AtOccurrence(clock, obj); color = obj.ForceId;
            location = obj.Position is { } position ? MapCoordinates.Cell(position) : before.PlaceCell(obj.Id) ?? after.PlaceCell(obj.Id);
            values["unit"] = MessageBinding.Reference(obj.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, obj.Id);
        }
        CommandMessage Produce(string role, bool report) => ProgressMessageProducer.Produce(
            (report ? content.Records : content.Feedback)[role], occurrence, texts, values, _ => 0);
        void Report(string role, MapReportImportance importance = MapReportImportance.Summary, string? sound = null)
        {
            var message = Produce(role, true);
            records.Add(new($"{identity}:{role}", message.Message, location, MapRecordColors.Resolve(before, after, color), importance) { SoundRole = sound });
        }
        void Prompt(string role, bool speech = false)
        {
            var message = Produce(role, false) with { FocusLocation = location };
            if (message.SpeakerId is int speaker)
            {
                message = message with { Speaker = occurrence.Speaker(speaker) };
                if (speech && executionMap && location is { } cell)
                    message = message with { WaitForMapSpeech = true, MapSpeech = new($"{identity}:{role}", message.Message,
                        speaker, texts.Resolve(before.MessageSpeaker(speaker).NameKey), cell,
                        ForceColorCode: before.Forces.Concat(after.Forces).FirstOrDefault(f => f.Id == color)?.ColorCode) };
            }
            messages.Add(message);
        }
        void Channel(string role) { if (executionMap) Report(role, sound: content.Feedback[role].Sound); else Prompt(role); }
        switch (fact)
        {
            case TacticLearnedEvent { Origin: TacticLearningOrigin.Practice } learned:
                At(learned.Occurrence); Person("actor", learned.OfficerId);
                values["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, learned.TacticId);
                if (Player(color)) { Prompt("tactic-learned"); Report("tactic-learned"); }
                else Report("tactic-learned-other", MapReportImportance.Detailed);
                break;
            case OfficerInjuryWorsenedEvent injury when Player(injury.Occurrence.ForceId) &&
                (!executionMap || before.World.Simulation.Boundary != SimulationBoundary.AwaitingDecision):
                At(injury.Occurrence); Person("actor", injury.OfficerId);
                values["injury"] = new(0, CommandText.Resolve(texts, injury.Current <= 4 ? "battle.injury-light" : "battle.injury-heavy"));
                Channel("injury-worsened"); break;
            case CommanderChangedEvent { PreviousId: not null, OfficerId: int commander } changed when Player(changed.ForceId):
                color = changed.ForceId; location = before.PlaceCell(changed.PlaceId) ?? after.PlaceCell(changed.PlaceId);
                Person("actor", commander);
                values["unit"] = MessageBinding.Reference(MessageEntityKind.Settlement, changed.PlaceId);
                Channel("place-commander-changed"); break;
            case UnitCommanderChangedEvent commander when Player(commander.Unit.ForceId):
                Object(commander.Unit); Person("actor", commander.OfficerId); Channel("unit-commander-changed"); break;
            case CorpsLeaderChangedEvent leader when Player(leader.Occurrence.ForceId):
                At(leader.Occurrence); Person("actor", leader.OfficerId);
                Channel("corps-leader-changed"); break;
            case CorpsDissolvedEvent dissolved when Player(dissolved.ForceId):
                if (events.OfType<ForcesConsolidatedEvent>().Any(e => e.AbsorbedId == dissolved.ForceId)) break;
                if (events.OfType<ForceSurrenderedEvent>().Any(e => e.ForceId == dissolved.ForceId)) break;
                color = dissolved.ForceId; Force("force", dissolved.ForceId); Channel("corps-reorganized"); break;
            case HighSpiritsExpiredEvent expired when Player(expired.Unit.ForceId) && expired.Unit.CommanderId is int commander:
                Object(expired.Unit); Person("actor", commander); values["current"] = new(expired.CurrentMorale);
                Report("high-spirits-expired"); Report("morale-lost"); break;
            case OfficerAppearedEvent { Status: OfficerStatus.Serving, ForceId: int owner } appeared when Player(owner):
                var person = before.World.Officers.Single(p => p.Id == appeared.OfficerId);
                var arrivalWorld = before.World with { Officers = before.World.Officers.Select(p => p.Id == appeared.OfficerId
                    ? p with { Status = appeared.Status, OwnerId = owner, SettlementId = appeared.SettlementId, RegionId = appeared.RegionId } : p).ToImmutableArray() };
                occurrence = context.ForWorld(arrivalWorld).AtOccurrence(clock);
                At(new(owner, appeared.SettlementId ?? appeared.RegionId, null)); Person("actor", person.Id);
                Person("ruler", before.World.Forces.Single(f => f.Id == owner).LeaderId!.Value);
                Prompt("appearance-introduction");
                messages[^1] = messages[^1] with { IllustrationPersonId = person.Id };
                Prompt("appearance-greeting", true); Prompt("appearance-result"); break;
            case TreasuryCapturedEvent captured when Player(captured.PreviousForceId) || Player(captured.ForceId):
                At(new(captured.ForceId, captured.PlaceId, captured.Position));
                Force("force", captured.PreviousForceId); Force("enemy-force", captured.ForceId);
                values["amount"] = new(captured.Money); values["food"] = new(captured.Food);
                if (executionMap) Report(Player(captured.ForceId) ? "treasury-captured" : "treasury-lost", MapReportImportance.Detailed);
                else Prompt("treasury-captured");
                break;
            case PrisonerEscapedEvent escaped when Player(escaped.Occurrence.ForceId) || Player(escaped.CaptorForceId):
                At(escaped.Occurrence); Person("actor", escaped.OfficerId);
                values["place"] = ProgressMessageProducer.Place(escaped.Occurrence.PlaceId!.Value);
                Prompt("prisoner-escape-speech", true); Prompt("prisoner-escape-result"); break;
            case BattlePersonnelResolvedEvent battle:
                Object(battle.Captor);
                if (battle.CapturedIds.Length > 0 && (Player(battle.Captor.ForceId) || Player(battle.Defeated.ForceId)))
                {
                    Force("force", Player(battle.Captor.ForceId) ? battle.Defeated.ForceId!.Value : battle.Captor.ForceId!.Value);
                    Person("actor", battle.CapturedIds[0]); values["count"] = new(battle.CapturedIds.Length);
                    color = Player(battle.Captor.ForceId) ? battle.Captor.ForceId : battle.Defeated.ForceId;
                    Report(Player(battle.Captor.ForceId) ? "battle-captured" : "battle-persons-lost");
                    if (battle.LastCapturedId is int last && battle.Captor.CommanderId is int receivingCommander)
                    {
                        Object(battle.Defeated); Person("actor", last); Person("ruler", receivingCommander);
                        Prompt(executionMap ? "battle-capture-speech" : "battle-capture-dialogue", true);
                    }
                }
                if (battle.RetreatedIds.Length > 0 && Player(battle.Defeated.ForceId))
                {
                    Object(battle.Defeated); Person("actor", battle.RetreatedIds[0]); values["count"] = new(battle.RetreatedIds.Length);
                    Report("battle-persons-retreated");
                }
                break;
        }
        return new(messages, records);
    }
}
