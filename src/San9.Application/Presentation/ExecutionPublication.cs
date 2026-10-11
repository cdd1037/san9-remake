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
        for (int index = 0; index < events.Count; index++)
        {
            if (presentedFacts?.Contains(index) == true) continue;
            var fact = events[index];
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision,$"publication:{index}"));
            int Pick(int count) => (int)(random.NextUInt64() % (ulong)count);
            if (content.Progress is { } progress)
                records.AddRange(ProgressMessages.Read(before,after,context,texts,progress,events,index));
            var mission = MissionPresentation.Results(before,texts,[fact],alreadyPresented,content.Missions,context,content.Decisions.People);
            messages.AddRange(mission.Messages);
            records.AddRange(mission.Records);
            if (content.Personnel is { } personnel && fact is SearchResolvedEvent or RecruitmentResolvedEvent)
            {
                var clock = fact is SearchResolvedEvent search ? search.Clock : ((RecruitmentResolvedEvent)fact).Clock;
                DomainEvent[] related = fact is RecruitmentResolvedEvent recruitment
                    ? new[] { fact }.Concat(events.OfType<PersonnelTerritoryTransferredEvent>().Where(e => e.OfficerId == recruitment.TargetOfficerId)).ToArray()
                    : [fact];
                messages.AddRange(PersonnelPresentation.Results(before,context.AtOccurrence(clock),texts,personnel,related,Pick));
            }
            if (fact is TaskCompletedEvent completed)
                messages.AddRange(ExecutionMessages.Read(before,context.AtOccurrence(completed.Clock),texts,content.Decisions,
                    new[] { fact }.Concat(events.OfType<TacticLearnedEvent>()).ToArray()));
        }
        return new(messages, records);
    }
}
