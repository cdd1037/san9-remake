using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal enum FeedbackKind { Outcome, Progress, Execution, Capture, Casualty, WorldEvent, Report, MapSpeech, Mission, Strategy, Personnel, Task }

internal sealed record FeedbackBatch(long Revision, int FactIndex, FeedbackKind Kind, ExecutionOutput Output)
{
    internal static string MessageId(long revision, int factIndex, FeedbackKind kind, int index) => $"feedback:{revision}:{factIndex}:{kind}:message:{index}";
    internal string MessageId(int index) => MessageId(Revision, FactIndex, Kind, index);
    internal string RecordId(int index) => $"feedback:{Revision}:{FactIndex}:{Kind}:record:{index}";
}

/// <summary>Owns feedback generation and identity; consumers choose only when each item is published.</summary>
internal static class OccurrenceFeedback
{
    internal static IReadOnlyList<FeedbackBatch> Read(StrategySnapshot before, StrategySnapshot occurrenceBefore,
        StrategySnapshot after, WorldMessageContext context, WorldMessageContext occurrenceContext,
        TextCatalog texts, CommandPresentation content, IReadOnlyList<DomainEvent> events, int index,
        IReadOnlySet<Guid>? alreadyPresented = null, bool executionMap = true)
    {
        List<FeedbackBatch> batches = [];
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        void Flush(FeedbackKind kind)
        {
            if (messages.Count + records.Count == 0) return;
            batches.Add(new(before.World.Revision, index, kind, new(messages.ToArray(), records.ToArray())));
            messages.Clear(); records.Clear();
        }
        var fact = events[index];
        if (content.Progress is { } feedback)
        {
            var outcome = OutcomeFeedback.Read(occurrenceBefore, after, occurrenceContext, texts, feedback, events, index, executionMap);
            messages.AddRange(outcome.Messages); records.AddRange(outcome.Records);
            Flush(FeedbackKind.Outcome);
            if (executionMap || fact is not OfficerAbilityGrownEvent)
                records.AddRange(ProgressFeedback.Records(occurrenceBefore, after, occurrenceContext, texts, feedback, events, index));
            if (!executionMap && fact is OfficerAbilityGrownEvent)
                messages.AddRange(ProgressFeedback.GrowthPrompts(occurrenceBefore, occurrenceContext, texts, feedback, [fact]));
        }
        Flush(FeedbackKind.Progress);
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"publication:{index}"));
        int Pick(int count) => (int)(random.NextUInt64() % (ulong)count);
        if (fact is OfficerDiedEvent { Cause: OfficerDeathCause.Execution } death && content.Execution is { } execution &&
            events.OfType<PrisonerExecutedEvent>().SingleOrDefault(e => e.OfficerId == death.OfficerId && e.Clock == death.Clock) is { } executed)
        {
            bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
            if (Player(executed.ExecutingForceId) || Player(death.PreviousForceId))
            {
                var sequence = ExecutionPresentation.Create(occurrenceContext.AtOccurrence(death.Clock), execution, executed, Pick);
                messages.AddRange(Player(executed.ExecutingForceId) ? sequence : sequence.Skip(2));
            }
        }
        Flush(FeedbackKind.Execution);
        if (content.Capture is { } captureContent && fact is SettlementCapturedEvent captured &&
            CapturePresentation.Create(occurrenceBefore, occurrenceContext, texts, captureContent, captured) is { } captureMessage)
            messages.Add(captureMessage);
        Flush(FeedbackKind.Capture);
        if (content.Events is { } eventContent)
        {
            if (fact is OfficerDiedEvent casualty && casualty.Cause is OfficerDeathCause.Melee or OfficerDeathCause.Sniping or OfficerDeathCause.Duel)
            {
                string role = casualty.Cause switch { OfficerDeathCause.Melee => "melee-announcement", OfficerDeathCause.Sniping => "sniping-announcement", _ => "duel-announcement" };
                messages.Add(ProgressMessageProducer.Produce(eventContent.Messages[role], occurrenceContext.AtOccurrence(casualty.Clock), texts,
                    new Dictionary<string, MessageBinding> { ["actor"] = ProgressMessageProducer.Person(casualty.OfficerId) }, Pick));
            }
            Flush(FeedbackKind.Casualty);
            var scene = WorldEventPresentation.ReadOccurrence(occurrenceBefore, after, occurrenceContext, texts, eventContent, events, index);
            messages.AddRange(scene.Messages); records.AddRange(scene.Records);
        }
        Flush(FeedbackKind.WorldEvent);
        if (content.Progress is { } progress)
        {
            var reportBefore = fact is ForceEliminatedEvent or ForceLeaderChangedEvent or OfficerDiedEvent
                ? occurrenceBefore : before;
            var reportContext = ReferenceEquals(reportBefore, before) ? context : occurrenceContext;
            records.AddRange(ProgressMessages.Read(reportBefore, after, reportContext, texts, progress, events, index));
            Flush(FeedbackKind.Report);
            messages.AddRange(MapNotificationSpeeches.Read(occurrenceBefore, after, occurrenceContext, texts, progress, fact, index));
        }
        Flush(FeedbackKind.MapSpeech);
        var mission = MissionPresentation.Results(before, texts, [fact], alreadyPresented, content.Missions, context, content.Decisions.People);
        messages.AddRange(mission.Messages);
        records.AddRange(mission.Records);
        Flush(FeedbackKind.Mission);
        var strategy = StrategyFeedback.Read(occurrenceBefore, after, occurrenceContext, texts, content, fact, index);
        messages.AddRange(strategy.Messages); records.AddRange(strategy.Records);
        Flush(FeedbackKind.Strategy);
        if (content.Personnel is { } personnel && fact is SearchResolvedEvent or RecruitmentResolvedEvent)
        {
            var clock = fact is SearchResolvedEvent search ? search.Clock : ((RecruitmentResolvedEvent)fact).Clock;
            DomainEvent[] related = fact is RecruitmentResolvedEvent recruitment
                ? new[] { fact }.Concat(events.OfType<PersonnelTerritoryTransferredEvent>().Where(e => e.OfficerId == recruitment.TargetOfficerId)).ToArray()
                : [fact];
            messages.AddRange(PersonnelPresentation.Results(before, context.AtOccurrence(clock), texts, personnel, related, Pick));
        }
        Flush(FeedbackKind.Personnel);
        if (fact is TaskCompletedEvent completed)
            messages.AddRange(ExecutionMessages.Read(before, context.AtOccurrence(completed.Clock), texts, content.Decisions,
                new[] { fact }.Concat(events.OfType<TacticLearnedEvent>()).ToArray()));
        Flush(FeedbackKind.Task);
        return batches;
    }
}
