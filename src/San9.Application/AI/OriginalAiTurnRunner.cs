using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal static class OriginalAiTurnRunner
{
    internal static readonly ImmutableArray<OriginalAiWork> RootFirst = [OriginalAiWork.Offices, OriginalAiWork.CreateCorps,
        OriginalAiWork.Retreat, OriginalAiWork.Deploy, OriginalAiWork.Gift, OriginalAiWork.Surrender, OriginalAiWork.Trade,
        OriginalAiWork.RecruitLocal, OriginalAiWork.ReturnPrisoner, OriginalAiWork.MoveOfficers, OriginalAiWork.Adviser,
        OriginalAiWork.ConfiscateTreasure, OriginalAiWork.DisposePrisoner, OriginalAiWork.GrantTreasure, OriginalAiWork.Reward, OriginalAiWork.RequestAttack];
    internal static readonly ImmutableArray<OriginalAiWork> ChildFirst = [OriginalAiWork.Retreat, OriginalAiWork.Deploy,
        OriginalAiWork.Trade, OriginalAiWork.RecruitLocal, OriginalAiWork.MoveOfficers];
    internal static readonly ImmutableArray<OriginalAiWork> BarbarianFirst = [OriginalAiWork.Retreat, OriginalAiWork.Deploy,
        OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Train, OriginalAiWork.Recruit, OriginalAiWork.Repair, OriginalAiWork.Gift];

    internal static OriginalAiState Run(SimulationEngine engine, ScenarioSessionDefinition definition, ControlSeat seat,
        OriginalAiState state, List<DomainEvent> events, List<OriginalAiWorkReport> reports)
    {
        state = OriginalAiStrategy.PrepareGlobal(engine.Snapshot, state);
        var force = engine.Snapshot.Forces.Single(f => f.Id == seat.ForceId);
        var geography = new OriginalAiGeography(definition.Content, definition.MilitaryRules, engine.Snapshot);
        // The Core seat authorizes this force. Within it each direct corps owns exactly one
        // economic pass; a delegated account never falls through to the root's projection.
        foreach (var selected in engine.Snapshot.Corps.Where(c => c.OwnerId == seat.ForceId)
            .OrderByDescending(c => c.IsRoot).ToArray())
        {
            var corps = engine.Snapshot.Corps.Single(c => c.Id == selected.Id);
            if (seat.Kind == ControllerKind.Player && corps.ControlMode == CorpsControlMode.Direct) continue;
            var context = new CommandContext(corps.ControlMode == CorpsControlMode.Automatic ? CorpsControl.ControllerId(corps.Id) : seat.ControllerId);
            var stream = new StableRandom(StableRandom.DeriveSeed(engine.Snapshot.Simulation.RunSeed,
                FormattableString.Invariant($"original-ai:{corps.Id}:{engine.Snapshot.Clock.DayNumber}")));
            var random = new OriginalAiRandom(stream);
            var strategy = OriginalAiStrategy.PrepareCorps(engine.Snapshot, corps, state, geography, random);
            state = state with { Corps = state.Corps.Where(c => c.CorpsId != corps.Id).Append(strategy).OrderBy(c => c.CorpsId).ToImmutableArray() };
            var forceState = state.Forces.Single(f => f.ForceId == seat.ForceId);
            var policy = state.Policy;
            var works = (force.IsBarbarian ? BarbarianFirst : corps.IsRoot ? RootFirst : ChildFirst)
                .Concat(force.IsBarbarian ? [] : policy.SecondaryBatches[strategy.Mode]).Append(OriginalAiWork.Residual);
            OriginalDomesticInputs Capture() => OriginalAiProjection.Domestic(engine, definition.Content,
                engine.Snapshot.Corps.Single(c => c.Id == corps.Id), geography, policy.RecruitmentEnabled);
            bool Submit(GameCommand command)
            {
                var result = engine.Execute(context, command);
                if (result.Submission.Accepted) events.AddRange(result.Events);
                return result.Submission.Accepted;
            }
            (Guid Id, long Sequence) Identity()
            {
                long sequence = engine.Snapshot.Simulation.NextSequence;
                return (SimulationIdentity.Command(sequence, seat.ForceId), sequence);
            }
            bool Develop(DevelopmentCommand development)
            {
                var id = Identity();
                return Submit(new DevelopCommand(id.Id, seat.ForceId, id.Sequence, development));
            }
            foreach (var work in works)
            {
                long revision = engine.Snapshot.Revision;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                void Report(OriginalAiWorkReport report) => reports.Add(report with { InputRevision = revision,
                    ElapsedTimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started,
                    AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
                if (!policy.WorkRules.TryGetValue(work, out var rule))
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.LaterStage, 0, 0));
                    continue;
                }
                if (force.IsBarbarian && !rule.AllowsBarbarians || !corps.IsRoot && !rule.AllowsChildCorps)
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.Permission, 0, 0));
                    continue;
                }
                if (!OriginalAiWorkGate.Calendar(engine.Snapshot.Clock, engine.Snapshot.Simulation.Started, forceState.SchedulingKey, rule, policy.Advanced))
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.Calendar, 0, 0));
                    continue;
                }
                int limit = OriginalAiWorkGate.Attempts(rule, policy.Advanced, forceState.Group);
                if (work <= OriginalAiWork.Train)
                {
                    var activity = work switch
                    {
                        OriginalAiWork.Patrol => DomesticActivity.Patrol,
                        OriginalAiWork.Commerce => DomesticActivity.Commerce,
                        OriginalAiWork.Cultivation => DomesticActivity.Cultivation,
                        OriginalAiWork.Repair => DomesticActivity.Repair,
                        OriginalAiWork.Recruit => DomesticActivity.Recruit,
                        OriginalAiWork.Train => DomesticActivity.Train,
                        _ => throw new InvalidOperationException("Unsupported domestic work."),
                    };
                    var result = OriginalDomesticWork.Run(activity, limit, Capture, () => random.Next(100), Develop);
                    var end = result.End switch
                    {
                        OriginalDomesticEnd.PreparationFailed => result.Steps.Any(s => s.ProbabilityFailed) ? OriginalAiWorkEnd.ProbabilityFailed : OriginalAiWorkEnd.PreparationFailed,
                        OriginalDomesticEnd.AttemptsExhausted => OriginalAiWorkEnd.AttemptsExhausted,
                        OriginalDomesticEnd.ReserveReached => OriginalAiWorkEnd.ReserveReached,
                        _ => OriginalAiWorkEnd.NoCandidates,
                    };
                    Report(new(corps.Id, work, end, result.Steps.Count(s => s.Outcome != OriginalDomesticAttempt.PreparationFailed),
                        result.Steps.Count(s => s.Outcome == OriginalDomesticAttempt.Submitted))
                        { Rejected = result.Steps.Count(s => s.Outcome == OriginalDomesticAttempt.Rejected) });
                }
                else if (work == OriginalAiWork.Trade)
                    Report(OriginalAiEconomy.Trade(corps.Id, limit, Capture, () => engine.Snapshot,
                        (place, people, direction) => engine.GetFoodTradeQuote(seat.ForceId, place, people, direction),
                        (place, people, plan) =>
                        {
                            var id = Identity();
                            return Submit(new FoodTradeCommand(id.Id, seat.ForceId, id.Sequence, place, people, plan.Direction, plan.FoodAmount));
                        }));
                else if (work == OriginalAiWork.Residual)
                    Report(OriginalAiEconomy.Residual(corps.Id, force.IsBarbarian, limit, Capture, () => engine.Snapshot, Develop,
                        (person, place) =>
                        {
                            var id = Identity();
                            return Submit(new SearchCommand(id.Id, seat.ForceId, id.Sequence, person, place));
                        }));
                else Report(new(corps.Id, work, OriginalAiWorkEnd.LaterStage, 0, 0));
            }
        }
        return state;
    }
}
