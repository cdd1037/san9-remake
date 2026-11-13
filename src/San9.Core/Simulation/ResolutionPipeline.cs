using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ResolutionStage
{
    TerminalCheck, XunEvents, XunUpdate, MonthEvents, AppearanceAndIndependence,
    QuarterPrelude, QuarterEvents, QuarterUpdate, AnnualAppearance, AnnualCleanup,
    DailyMovement, BeforeDailyCommands, DailyCombat, DailyCommands, UnitAndBuildingEvents, QuarterStudyOffers, QuarterPolitics, XunCleanup,
}

public sealed record StageCompletedEvent(GameClock Clock, ResolutionStage Stage) : DomainEvent;
public sealed record TaskStartedEvent(PersistentTask Task) : DomainEvent;
public sealed record TaskCompletedEvent(Guid TaskId, GameClock Clock) : DomainEvent;
public sealed record TaskCancelledEvent(Guid TaskId, GameClock Clock, string Reason) : DomainEvent;
public sealed record SimulationEndedEvent(GameClock Clock, string Reason) : DomainEvent;
public sealed record ControlEndedEvent(string ControllerId, int ForceId) : DomainEvent;

internal abstract class TaskRule
{
    internal virtual TaskResolutionPhase Phase => TaskResolutionPhase.DailyCommands;
    // Calendar-day offset from submission to this rule's resolution phase.
    internal abstract int DurationDays(PersistentTask task);
    internal virtual bool HasValidPayload(PersistentTask task) => task.Journey is null;
    internal virtual bool SupportsResolutionStep(int step) => step == 0;
    internal abstract bool Validate(WorldMutation world, PersistentTask task);
    internal virtual int? DecisionRecipient(WorldMutation world, PersistentTask task) => null;
    internal virtual bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) => response.Offer is null && response.CommunitySearch is null && response.SearchConflict is null;
    internal virtual PersistentTask? ResolveResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response,
        List<DomainEvent> events) => Resolve(world, task, response.Accept, events);
    internal virtual void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events) =>
        throw new InvalidOperationException("Task does not accept a decision.");
    // The pipeline owns task lifetime. A returned task retains the mission and advances
    // to its next resolution step; null finishes it. Automatic and player steps share this path.
    internal virtual PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
    {
        if (accept is bool answer) CompleteDecision(world, task, answer, events);
        else Complete(world, task, events);
        return null;
    }
    internal virtual void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        throw new InvalidOperationException("Task requires staged resolution.");
}

internal sealed class ResolutionPipeline(SimulationRules rules)
{
    internal Action<ResolutionTransition>? Observer { get; set; }

    internal void Transition(ResolutionPhase phase, WorldMutation world, List<DomainEvent> events)
    {
        if (Observer is { } observer) observer(new(phase, events.Count, world.CapturePresentation()));
    }

    internal void Prepare(WorldMutation world, List<DomainEvent> events, ResolutionStage? after = null)
    {
        if (after is null) Transition(ResolutionPhase.XunOpening, world, events);
        var boundary = world.Clock.Boundary;
        List<ResolutionStage> stages = [ResolutionStage.TerminalCheck, ResolutionStage.XunEvents, ResolutionStage.XunUpdate];
        if (boundary.Month) stages.Add(ResolutionStage.MonthEvents);
        stages.Add(ResolutionStage.AppearanceAndIndependence);
        if (boundary.Quarter) stages.AddRange([ResolutionStage.QuarterPrelude, ResolutionStage.QuarterEvents, ResolutionStage.QuarterStudyOffers, ResolutionStage.QuarterUpdate]);
        if (boundary.Year) stages.AddRange([ResolutionStage.AnnualAppearance, ResolutionStage.AnnualCleanup]);
        stages.Add(ResolutionStage.XunCleanup);
        if (boundary.Quarter) stages.Add(ResolutionStage.QuarterPolitics);
        int start = after is { } completed ? stages.IndexOf(completed) + 1 : 0;
        if (after is not null && start == 0) throw new InvalidOperationException("Invalid preparation continuation.");
        foreach (var stage in stages.Skip(start))
            if (!Run(stage, world, events)) return;
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = 0 });
        ThreeVisits.EnterControl(world, rules, events);
    }

    internal void AdvanceDay(WorldMutation world, List<DomainEvent> events) =>
        AdvanceDay(world, events, new(world.Clock.Year, world.Clock.Month, world.Clock.Xun));

    internal void AdvanceDay(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        ImmutableArray<Guid>? remainingTasks = null)
    {
        if (remainingTasks is { } tasks)
        {
            CompleteDailyCommands(world, events, xunStarted, tasks);
            return;
        }
        world.MaintainDerivedState(events);
        if (!CheckTerminal(world, events)) return;
        var contactsBefore = rules.Military is { } military ? world.Engagements(military) : null;
        MilitaryPolicies.AdvanceDay(world, rules.Military, events);
        world.MaintainDerivedState(events);
        var arrivals = MilitaryMovement.AdvanceDay(world, rules.Military, events);
        world.MaintainDerivedState(events);
        if (!CompleteStage(ResolutionStage.DailyMovement, world, events)) return;

        var rationAccounts = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, world, rules.Economy, events);
        MilitaryTactics.TickCooldowns(world);
        MilitaryResearch.AdvanceDay(world, rules.Military, events);
        FormationTeaching.AdvanceDay(world, events);
        EquipmentProduction.AdvanceDay(world, rules.Equipment, events);
        ForceLifecycle.RetryArmamentTransfers(world, events);
        ArmamentProduction.AdvanceDay(world, rules.Military, events);
        MilitaryDailyCycle.Apply(world, rationAccounts, rules, events);
        BattleLearning.Daily(world, rules.Military, events);
        MilitaryMovement.BeginDailyCycle(world);
        MilitaryPolicies.FinishPursuitResponses(world);
        MilitaryConditions.Tick(world, events);
        MilitaryMovement.TickDepartureDelays(world);
        MilitaryPolicies.ApproachDay(world, rules.Military, events);
        world.MaintainDerivedState(events);
        if (contactsBefore is not null) BattleContact.PublishEntries(world, rules.Military!, contactsBefore, events);
        world.CurrentDateCountdownConsumed = true;
        if (!CompleteStage(ResolutionStage.BeforeDailyCommands, world, events)) return;

        MilitaryMovement.ResolveArrivals(world, arrivals, rules.Military, rules.Governance, rules.Personnel, events);
        world.MaintainDerivedState(events);
        FacilityConstruction.AdvanceDay(world, rules, events);
        world.MaintainDerivedState(events);
        if (rules.Military is { } engagementRules) MilitaryEngagements.ClearExpired(world, engagementRules);
        MilitaryCombat.AdvanceDay(world, rules, events, xunStarted);
        CompleteDailyCombat(world, events, xunStarted);
    }

    private void CompleteDailyCommands(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        ImmutableArray<Guid>? remainingTasks = null)
    {
        ResolveTasks(world, events, xunStarted, remainingTasks: remainingTasks);
        if (world.Simulation.PendingDailyTask is not null) return;
        if (!CheckTerminal(world, events) || !Run(ResolutionStage.DailyCommands, world, events)) return;
        world.SetClock(world.Clock.NextDay());
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks
            .Select(t => t with { ProgressDays = checked(t.ProgressDays + 1) }).ToImmutableArray() });
        FinishDay(world, events, xunStarted);
    }

    private void FinishDay(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        ImmutableArray<Guid>? remainingTasks = null)
    {
        if (world.Clock.IsXunBoundary)
        {
            if (remainingTasks is null) Transition(ResolutionPhase.XunClosing, world, events);
            ResolveTasks(world, events, xunStarted, phase: TaskResolutionPhase.XunBoundary, remainingTasks: remainingTasks);
            if (world.Simulation.PendingDailyTask is not null || !CheckTerminal(world, events)) return;
        }
        FinishUnitAndBuildingEvents(world, events, xunStarted);
    }

    internal void FinishUnitAndBuildingEvents(WorldMutation world, List<DomainEvent> events, GameClock xunStarted, int? afterRuler = null)
    {
        if (!CheckTerminal(world, events)) return;
        CapturedRulerDisposition.Apply(world, events, xunStarted, afterRuler);
        if (world.Simulation.HasPendingDailyResolution || !CheckTerminal(world, events)) return;
        // The later unit/building phase settles returns already due in the completed day,
        // including those created by that day's commands. Advancing the date is not another countdown tick.
        // A zero-distance return created by diplomacy or a captured ruler disposition can also settle immediately.
        ResolveTasks(world, events, xunStarted, returningAfterDateAdvance: true);
        Run(ResolutionStage.UnitAndBuildingEvents, world, events);
    }

    private bool Run(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
    {
        world.MaintainDerivedState(events);
        if (stage == ResolutionStage.TerminalCheck && rules.Terminal is not null)
        {
            var result = TerminalEvaluation.Evaluate(world.Clock, world.Simulation, world.Forces.Values, world.Settlements.Values, rules.Terminal);
            if (result.Route != TerminalRoute.Continue) world.SetSimulation(world.Simulation with { TerminalReason = result.Reason });
        }
        if (!CheckTerminal(world, events)) return false;
        if (stage == ResolutionStage.XunEvents && !ChainedPlot.Apply(world, rules, events) && !ThreeVisits.Expire(world, rules, events))
            RedCliffs.Apply(world, rules, events);
        if (stage == ResolutionStage.XunUpdate)
        {
            ForceConsolidation.Apply(world, rules, events);
            SettlementDefections.Apply(world, rules, events);
            UnitDefections.Apply(world, rules, events);
            if (!CheckTerminal(world, events)) return false;
            AttackRequests.UpdateXun(world, events);
            AllianceDiplomacy.UpdateXun(world, events);
            OfficerInjuryRecovery.Apply(world, events);
            XunRecovery.Settlements(world);
            PrisonerCycle.Apply(world, events);
            FreeOfficerMigration.Apply(world, rules.Personnel, events);
        }
        if (stage == ResolutionStage.AppearanceAndIndependence)
        {
            ForceEmergence.Apply(world, rules, events);
            CityIndependence.Apply(world, rules, events);
        }
        if (stage == ResolutionStage.XunCleanup)
        {
            XunRecovery.Personnel(world);
            XunRecovery.Units(world);
            EmperorTransfer.Begin(world, rules, events);
        }
        if (stage == ResolutionStage.QuarterPolitics) PoliticalEvents.Begin(world, rules, events);
        if (stage == ResolutionStage.QuarterEvents) QuarterHistoricalEvents.Begin(world, rules, events);
        if (stage == ResolutionStage.QuarterStudyOffers)
        {
            TacticStudy.CompleteQuarter(world, rules, events);
            TacticStudy.Offer(world, rules, events);
        }
        if (stage == ResolutionStage.QuarterPrelude)
        {
            ImperialCourt.ApplyDeaths(world, rules, events);
            if (!CheckTerminal(world, events)) return false;
        }
        if (stage == ResolutionStage.QuarterUpdate)
        {
            NaturalMortality.Apply(world, rules, events);
            if (!CheckTerminal(world, events)) return false;
            PersonnelLoyaltyCycle.Apply(world, events);
            ForceRelations.AdvanceQuarter(world, rules.Military, events);
        }
        if (stage == ResolutionStage.AnnualAppearance) OfficerAppearance.AdvanceYear(world, rules.Personnel, events);
        EconomicCycle.Apply(stage, world, rules.Economy, events);
        if (stage == ResolutionStage.XunUpdate) WoundedRecovery.Apply(world, rules.Military, events);
        if (stage == ResolutionStage.XunUpdate) PrisonerEscapeCycle.Apply(world, rules.Personnel, rules.Economy, events);
        if (stage == ResolutionStage.XunUpdate)
        {
            OfficeRequests.Begin(world, rules, events);
        }
        return CompleteStage(stage, world, events);
    }

    internal void ResumeDuel(WorldMutation world, List<DomainEvent> events, int successorId)
    {
        world.CurrentDateCountdownConsumed = true;
        var xunStarted = world.Simulation.PendingDuel!.XunStarted;
        MilitaryCombat.ResumeDuel(world, rules, events, successorId);
        CompleteDailyCombat(world, events, xunStarted);
    }

    internal void ResumeSniping(WorldMutation world, List<DomainEvent> events, int successorId)
    {
        world.CurrentDateCountdownConsumed = true;
        var xunStarted = world.Simulation.PendingSniping!.Exchange!.XunStarted;
        MilitaryCombat.ResumeSniping(world, rules, events, successorId);
        CompleteDailyCombat(world, events, xunStarted);
    }

    private void CompleteDailyCombat(WorldMutation world, List<DomainEvent> events, GameClock xunStarted)
    {
        world.MaintainDerivedState(events);
        if (world.Simulation.PendingDuel is not null || world.Simulation.PendingSniping is not null || !CheckTerminal(world, events)) return;
        TacticSupport.AdvanceDay(world, rules, events);
        world.MaintainDerivedState(events);
        MilitaryOrders.ResolveOrders(world, rules.Military, events);
        world.MaintainDerivedState(events);
        if (!CompleteStage(ResolutionStage.DailyCombat, world, events)) return;
        CompleteDailyCommands(world, events, xunStarted);
    }

    private bool CompleteStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
    {
        rules.ApplyStage(stage, world, events);
        AttackRequests.Cleanup(world, events);
        CorpsDelegations.Cleanup(world, rules.Military, events);
        world.MaintainDerivedState(events);
        events.Add(new StageCompletedEvent(world.Clock, stage));
        return CheckTerminal(world, events) && world.Simulation.PendingEvent is null && world.Simulation.PendingTacticStudy is null;
    }

    internal static bool CheckTerminal(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.TerminalReason is not string reason) return true;
        foreach (var task in world.Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id))
        {
            ReinforcementTransport.Disband(world, task, "terminal", events);
            TaskCancellation.Apply(world, task, "terminal", events);
        }
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Terminal, Tasks = [], PendingEvent = null, PendingDailyTask = null,
            PendingSniping = null, PendingCapturedRuler = null, PendingExecutionSuccession = null, PendingTacticStudy = null, PendingDuel = null });
        if (!events.OfType<SimulationEndedEvent>().Any()) events.Add(new SimulationEndedEvent(world.Clock, reason));
        return false;
    }

    internal static void CheckConquest(WorldMutation world, List<DomainEvent> events)
    {
        if (!events.OfType<ForceEliminatedEvent>().Any()) return;
        if (world.Simulation.TerminalReason is not null) return;
        if (events.OfType<ForceEliminatedEvent>().Any(e => e.WasPlayerControlled) &&
            !world.Simulation.Controls.Any(c => c.Kind == ControllerKind.Player))
            world.SetSimulation(world.Simulation with { TerminalReason = "player-defeated" });
        else if (world.Forces.Values.Count(f => !f.IsEliminated && !f.IsBarbarian) == 1)
            world.SetSimulation(world.Simulation with { TerminalReason = "conquest" });
    }

    internal void Respond(WorldMutation world, RespondDailyTaskCommand response, List<DomainEvent> events)
    {
        var pending = world.Simulation.PendingDailyTask!;
        world.CurrentDateCountdownConsumed = pending.Phase == TaskResolutionPhase.DailyCommands;
        var task = world.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
        var rule = rules.FindTask(task.Kind)!;
        world.SetSimulation(world.Simulation with { PendingDailyTask = null, Boundary = SimulationBoundary.Stable });
        if (world.Officers.TryGetValue(task.OfficerId, out var officer)) world.Replace(officer with { Acted = true });
        if (TaskReferencesValid(world, task) && rule.Validate(world, task))
        {
            ResolveStep(world, task, rule, response, events);
        }
        else
        {
            TaskCancellation.Apply(world, task, "invalid-target-or-executor", events, preserveCargo: task.Journey is not null);
            if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
        }
        CorpsDelegations.Cleanup(world, rules.Military, events);
        world.MaintainDerivedState(events);
        if (!CheckTerminal(world, events)) return;
        if (pending.Phase == TaskResolutionPhase.XunBoundary)
            FinishDay(world, events, pending.XunStarted, pending.RemainingTaskIds);
        else AdvanceDay(world, events, pending.XunStarted, pending.RemainingTaskIds);
    }

    private void ResolveTasks(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        bool returningAfterDateAdvance = false, ImmutableArray<Guid>? remainingTasks = null,
        TaskResolutionPhase phase = TaskResolutionPhase.DailyCommands)
    {
        var queue = remainingTasks ?? world.Simulation.Tasks
            .Where(t => returningAfterDateAdvance
                ? t.Kind == PersonnelReturns.Kind && (t.Due.DayNumber < world.Clock.DayNumber ||
                    t.Due == world.Clock && t.Journey?.ReturnLeg?.CountdownDays == 0)
                : rules.FindTask(t.Kind)?.Phase == phase)
            .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id)
            .Select(t => t.Id).ToImmutableArray();
        for (int index = 0; index < queue.Length; index++)
        {
            var task = world.Simulation.Tasks.FirstOrDefault(t => t.Id == queue[index]);
            if (task is null) continue;
            var rule = rules.FindTask(task.Kind) ?? throw new InvalidOperationException($"Unsupported task '{task.Kind}'.");
            bool valid = TaskReferencesValid(world, task) && rule.Validate(world, task);
            if (valid && world.Clock.DayNumber < task.Due.DayNumber) continue;
            if (valid && !returningAfterDateAdvance && rule.DecisionRecipient(world, task) is int recipient)
            {
                world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision,
                    PendingDailyTask = new(xunStarted, recipient, queue.Skip(index)
                        .Where(id => world.Simulation.Tasks.Any(t => t.Id == id)).ToImmutableArray()) { Phase = phase } });
                return;
            }
            if (world.Officers.TryGetValue(task.OfficerId, out var officer))
                world.Replace(officer with { Acted = true });
            if (valid)
            {
                if (ResolveStep(world, task, rule, null, events)) index--;
            }
            else
            {
                TaskCancellation.Apply(world, task, "invalid-target-or-executor", events, preserveCargo: task.Journey is not null);
                if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
            }
            CorpsDelegations.Cleanup(world, rules.Military, events);
            world.MaintainDerivedState(events);
            if (!CheckTerminal(world, events)) return;
        }
    }

    private static bool ResolveStep(WorldMutation world, PersistentTask task, TaskRule rule, RespondDailyTaskCommand? response, List<DomainEvent> events)
    {
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
        var next = response is null ? rule.Resolve(world, task, null, events) : rule.ResolveResponse(world, task, response, events);
        if (next is null)
        {
            events.Add(new TaskCompletedEvent(task.Id, world.Clock));
            return false;
        }
        if (next.Id != task.Id || next.Kind != task.Kind || next.ControllerId != task.ControllerId ||
            next.ForceId != task.ForceId || next.OfficerId != task.OfficerId || next.OriginSettlementId != task.OriginSettlementId ||
            next.Started != task.Started || next.Due != task.Due || next.ProgressDays != task.ProgressDays || next.Sequence != task.Sequence ||
            next.ResolutionStep != task.ResolutionStep)
            throw new InvalidOperationException("A task transition must preserve mission identity and travel schedule.");
        next = next with { ResolutionStep = checked(task.ResolutionStep + 1) };
        if (!rule.SupportsResolutionStep(next.ResolutionStep) || !rule.HasValidPayload(next))
            throw new InvalidOperationException("Invalid next task resolution step or payload.");
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(next) });
        return true;
    }

    internal static bool TaskReferencesValid(WorldMutation world, PersistentTask task) =>
        world.Forces.ContainsKey(task.ForceId) &&
        world.Officers.TryGetValue(task.OfficerId, out var officer) && officer.IsServing && officer.OwnerId == task.ForceId &&
        officer.SettlementId is int home && (task.Journey is not null || home == task.OriginSettlementId) &&
        world.Settlements.TryGetValue(home, out var target) &&
        target.OwnerId == task.ForceId && target.CanHostPersons && target.Durability > 0;
}
