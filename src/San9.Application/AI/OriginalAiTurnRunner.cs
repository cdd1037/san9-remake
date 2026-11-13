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
        OriginalAiState state, List<DomainEvent> events, List<OriginalAiWorkReport> reports, OriginalAiTerrainRoutes terrainRoutes)
    {
        var cursor = state.Continuation;
        if (cursor is null)
        {
            state = OriginalAiStrategy.PrepareGlobal(engine.Snapshot, state);
            OriginalAiResearch.TryStart(engine, definition.MilitaryRules, seat, events, definition.Rules.Equipment);
            OriginalAiFormationTeaching.TryStart(engine, definition.MilitaryRules, seat, events);
            OriginalAiEquipment.Manage(engine, definition.Rules.Equipment, seat, events);
            OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);
            OriginalAiPersonalPeerage.Manage(engine, seat, events);
            PlayerDomesticAutomation.Run(engine, definition, seat, state, events, reports);
            cursor = new(seat, engine.Snapshot.Corps.Where(c => c.OwnerId == seat.ForceId)
                .OrderByDescending(c => c.IsRoot).Select(c => c.Id).ToImmutableArray(), [], 0, false, 0,
                ImmutableDictionary<int, long>.Empty, null);
        }
        else if (cursor.Seat != seat) throw new InvalidOperationException("Original AI continuation belongs to another control.");
        var force = engine.Snapshot.Forces.Single(f => f.Id == seat.ForceId);
        bool advanced = engine.Snapshot.Simulation.NewGame.Difficulty == GameDifficulty.Advanced;
        var geographyContext = new OriginalAiGeographyContext(definition.Content, definition.MilitaryRules);
        var geography = geographyContext.Get(engine.Snapshot);
        while (!cursor.Queue.IsEmpty && engine.Snapshot.Simulation.Boundary == SimulationBoundary.Stable)
        {
            int selected = cursor.Queue[0];
            var corps = engine.Snapshot.Corps.FirstOrDefault(c => c.Id == selected);
            if (corps is null || seat.Kind == ControllerKind.Player && corps.ControlMode == CorpsControlMode.Direct)
            {
                cursor = cursor with { Queue = cursor.Queue.RemoveAt(0), Seen = cursor.Seen.Add(selected), Prepared = false, WorkIndex = 0 };
                continue;
            }
            geography = geographyContext.Get(engine.Snapshot);
            var context = new CommandContext(corps.ControlMode == CorpsControlMode.Automatic ? CorpsControl.ControllerId(corps.Id) : seat.ControllerId);
            var stream = new StableRandom(cursor.Prepared ? cursor.RandomState : StableRandom.DeriveSeed(engine.Snapshot.Simulation.RunSeed,
                FormattableString.Invariant($"original-ai:{corps.Id}:{engine.Snapshot.Clock.DayNumber}")));
            var random = new OriginalAiRandom(stream);
            var strategy = cursor.Prepared ? state.Corps.Single(c => c.CorpsId == corps.Id) :
                OriginalAiStrategy.PrepareCorps(engine.Snapshot, corps, state, geography, random);
            if (!cursor.Prepared)
            {
                state = state with { Corps = state.Corps.Where(c => c.CorpsId != corps.Id).Append(strategy).OrderBy(c => c.CorpsId).ToImmutableArray() };
                cursor = cursor with { Prepared = true, Quotas = OriginalAiStaffing.Quotas(engine.Snapshot, corps.Id, geography) };
            }
            var forceState = state.Forces.Single(f => f.ForceId == seat.ForceId);
            var policy = state.Policy;
            var works = (force.IsBarbarian ? BarbarianFirst : corps.IsRoot ? RootFirst : ChildFirst)
                .Concat(force.IsBarbarian ? [] : policy.SecondaryBatches[strategy.Mode]).Append(OriginalAiWork.Residual).ToArray();
            var domestic = new OriginalAiProjection(engine, definition.Content, corps.Id, geography, policy.RecruitmentEnabled);
            OriginalDomesticInputs Capture() => domestic.Domestic();
            bool Submitted(CommandExecutionResult result)
            {
                if (result.Submission.Accepted)
                {
                    events.AddRange(result.Events);
                    AutomaticResponseRunner.Run(engine, events);
                }
                return result.Submission.Accepted;
            }
            bool Submit(GameCommand command) => Submitted(engine.Execute(context, command));
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
            var personnel = new OriginalAiPersonnel(engine, definition.Content, corps.Id, geography, context, random, Submit);
            for (int workIndex = cursor.WorkIndex; workIndex < works.Length; workIndex++)
            {
                if (!ReferenceEquals(geography, geographyContext.Get(engine.Snapshot)))
                {
                    geography = geographyContext.Get(engine.Snapshot);
                    domestic = new(engine, definition.Content, corps.Id, geography, policy.RecruitmentEnabled);
                    personnel = new(engine, definition.Content, corps.Id, geography, context, random, Submit);
                    cursor = cursor with { Quotas = OriginalAiStaffing.Quotas(engine.Snapshot, corps.Id, geography) };
                }
                var work = works[workIndex];
                long revision = engine.Snapshot.Revision;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                void Report(OriginalAiWorkReport report) => reports.Add(report with { InputRevision = revision,
                    ElapsedTimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started,
                    AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
                if (!policy.WorkRules.TryGetValue(work, out var rule))
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.PreparationFailed, 0, 0));
                    continue;
                }
                if (force.IsBarbarian && !rule.AllowsBarbarians || !corps.IsRoot && !rule.AllowsChildCorps ||
                    !OriginalAiActionPermissions.Allows(engine.Snapshot, engine.Companion, seat.ForceId, work))
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.Permission, 0, 0));
                    continue;
                }
                bool unrestrictedRewardTiming = seat.Kind == ControllerKind.Automatic && work == OriginalAiWork.Reward &&
                    engine.Companion.DisableComputerRewardTimingRestrictions;
                if (!unrestrictedRewardTiming &&
                    !OriginalAiWorkGate.Calendar(engine.Snapshot.Clock, engine.Snapshot.Simulation.Started, forceState.SchedulingKey, rule, advanced))
                {
                    Report(new(corps.Id, work, OriginalAiWorkEnd.Calendar, 0, 0));
                    continue;
                }
                int limit = OriginalAiWorkGate.Attempts(rule, advanced, forceState.Group);
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
                    Report(result.ToReport(corps.Id, work));
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
                            return Submit(new SearchCommand(id.Id, seat.ForceId, id.Sequence, [person], place));
                        }, domestic.Observe));
                else if (work == OriginalAiWork.CreateCorps)
                {
                    var organization = OriginalAiStaffing.Organize(engine, definition.Content, corps.Id, geography, Submit);
                    Report(organization);
                    if (organization.Submitted > 0)
                    {
                        cursor = cursor with { Quotas = OriginalAiStaffing.Quotas(engine.Snapshot, corps.Id, geography) };
                        var pending = cursor.Queue.Where(id => engine.Snapshot.Corps.Any(c => c.Id == id)).ToImmutableArray();
                        cursor = cursor with { Queue = pending.AddRange(engine.Snapshot.Corps.Where(c => c.OwnerId == seat.ForceId &&
                            !cursor.Seen.Contains(c.Id) && !pending.Contains(c.Id)).Select(c => c.Id)) };
                        state = state.Reconcile(engine.Snapshot);
                    }
                }
                else if (OriginalAiPersonnel.Handles(work))
                    Report(personnel.Run(work, limit, state, cursor.Quotas));
                else if (OriginalAiActions.Handles(work))
                    Report(new OriginalAiActions(engine, definition.Content with { OriginalAi = policy }, definition.MilitaryRules,
                        corps.Id, context, random, Submit).Run(work, limit, state, strategy));
                else if (OriginalAiMilitary.Handles(work))
                    Report(definition.MilitaryRules is { } military
                        ? new OriginalAiMilitary(engine, definition.Content with { OriginalAi = policy }, military, definition.Rules,
                            geographyContext, terrainRoutes, corps.Id, context, random, (command, prepared) => Submitted(prepared is null
                                ? engine.Execute(context, command) : engine.Execute(context, (DeployUnitCommand)command, prepared))).Run(work, limit, state, strategy)
                        : new(corps.Id, work, OriginalAiWorkEnd.PreparationFailed, 0, 0));
                else if (work == OriginalAiWork.DisposePrisoner)
                {
                    var progress = personnel.Dispose(cursor.Prisoners ?? personnel.PreparePrisoners(), limit);
                    if (engine.Snapshot.Simulation.Boundary == SimulationBoundary.AwaitingDecision)
                        return state with { Continuation = cursor with { WorkIndex = workIndex, RandomState = stream.State, Prisoners = progress } };
                    Report(new(corps.Id, work, progress.Attempts >= limit ? OriginalAiWorkEnd.AttemptsExhausted : OriginalAiWorkEnd.NoCandidates,
                        progress.Attempts, progress.Submitted) { Rejected = progress.Rejected, EmptyPlans = progress.EmptyPlans });
                    cursor = cursor with { Prisoners = null };
                }
                else throw new InvalidOperationException($"No production executor for original AI work {work}.");
                if (engine.Snapshot.Simulation.Boundary == SimulationBoundary.Terminal) return state with { Continuation = null };
            }
            cursor = cursor with { Queue = cursor.Queue.RemoveAt(0), Seen = cursor.Seen.Add(corps.Id), Prepared = false, WorkIndex = 0 };
        }
        return state with { Continuation = null };
    }
}
