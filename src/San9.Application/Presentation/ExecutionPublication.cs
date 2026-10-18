using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Projects committed facts in their original order, once per publication.</summary>
public static class ExecutionPublication
{
    public static ExecutionOutput Read(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, CommandPresentation content,
        IReadOnlyList<DomainEvent> events, IReadOnlySet<Guid>? alreadyPresented = null, IReadOnlySet<int>? presentedFacts = null)
    {
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        var identities = new PresentationIdentity(before, after);
        var occurrenceBefore = before;
        var occurrenceContext = context;
        for (int index = 0; index < events.Count; index++)
        {
            // Presented facts still update the identity of subsequent facts.
            if (index > 0) identities.Apply(events[index - 1]);
            if (!ReferenceEquals(occurrenceBefore, identities.View))
            {
                occurrenceBefore = identities.View;
                occurrenceContext = context.ForWorld(occurrenceBefore.World);
            }
            if (presentedFacts?.Contains(index) == true) continue;
            var fact = events[index];
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
            if (content.Events is { } eventContent)
            {
                if (fact is OfficerDiedEvent casualty && casualty.Cause is OfficerDeathCause.Melee or OfficerDeathCause.Sniping or OfficerDeathCause.Duel)
                {
                    string role = casualty.Cause switch { OfficerDeathCause.Melee => "melee-announcement", OfficerDeathCause.Sniping => "sniping-announcement", _ => "duel-announcement" };
                    messages.Add(ProgressMessageProducer.Produce(eventContent.Messages[role], occurrenceContext.AtOccurrence(casualty.Clock), texts,
                        new Dictionary<string, MessageBinding> { ["actor"] = ProgressMessageProducer.Person(casualty.OfficerId) }, Pick));
                }
                var scene = WorldEventPresentation.ReadOccurrence(occurrenceBefore, after, occurrenceContext, texts, eventContent, events, index);
                messages.AddRange(scene.Messages); records.AddRange(scene.Records);
            }
            if (content.Progress is { } progress)
            {
                var reportBefore = fact is ForceEliminatedEvent or ForceLeaderChangedEvent or OfficerDiedEvent
                    ? occurrenceBefore : before;
                var reportContext = ReferenceEquals(reportBefore, before) ? context : occurrenceContext;
                records.AddRange(ProgressMessages.Read(reportBefore, after, reportContext, texts, progress, events, index));
                messages.AddRange(MapNotificationSpeeches.Read(before, after, context, texts, progress, fact, index));
            }
            var mission = MissionPresentation.Results(before, texts, [fact], alreadyPresented, content.Missions, context, content.Decisions.People);
            messages.AddRange(mission.Messages);
            records.AddRange(mission.Records);
            if (content.Personnel is { } personnel && fact is SearchResolvedEvent or RecruitmentResolvedEvent)
            {
                var clock = fact is SearchResolvedEvent search ? search.Clock : ((RecruitmentResolvedEvent)fact).Clock;
                DomainEvent[] related = fact is RecruitmentResolvedEvent recruitment
                    ? new[] { fact }.Concat(events.OfType<PersonnelTerritoryTransferredEvent>().Where(e => e.OfficerId == recruitment.TargetOfficerId)).ToArray()
                    : [fact];
                messages.AddRange(PersonnelPresentation.Results(before, context.AtOccurrence(clock), texts, personnel, related, Pick));
            }
            if (fact is TaskCompletedEvent completed)
                messages.AddRange(ExecutionMessages.Read(before, context.AtOccurrence(completed.Clock), texts, content.Decisions,
                    new[] { fact }.Concat(events.OfType<TacticLearnedEvent>()).ToArray()));
        }
        return new(messages, records);
    }
}
