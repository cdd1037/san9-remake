using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum ResolutionStage
{
    TerminalCheck, XunEvents, XunUpdate, MonthEvents, AppearanceAndIndependence,
    QuarterPrelude, QuarterEvents, QuarterUpdate, AnnualAppearance, AnnualCleanup,
    BeforeDailyCommands, DailyCommands, DailyMapUpdate, UnitAndBuildingEvents,
}

public sealed record StageCompletedEvent(GameClock Clock, ResolutionStage Stage) : DomainEvent;
public sealed record TaskStartedEvent(PersistentTask Task) : DomainEvent;
public sealed record TaskCompletedEvent(Guid TaskId, GameClock Clock) : DomainEvent;
public sealed record TaskCancelledEvent(Guid TaskId, GameClock Clock, string Reason) : DomainEvent;
public sealed record SimulationEndedEvent(GameClock Clock, string Reason) : DomainEvent;
public sealed record ControlEndedEvent(string ControllerId, int ForceId) : DomainEvent;

// Rules are compiled domain code, not callbacks loaded from content or saves.
internal class SimulationRules
{
    internal EconomyRulebook Economy { get; init; } = EconomyRulebook.Empty;
    internal TreasureRulebook Treasures { get; init; } = TreasureRulebook.Empty;
    internal PersonnelRulebook Personnel { get; init; } = PersonnelRulebook.Empty;
    internal GovernanceRulebook Governance { get; init; } = new([]);
    internal MilitaryRulebook? Military { get; init; }
    internal OfficerAbilityRules Abilities => new(Treasures, Governance);
    internal virtual TaskRule? FindTask(string kind) => kind is PersonnelMissions.RecruitKind or PersonnelMissions.MoveKind or PersonnelMissions.SearchKind
        ? new PersonnelMissionRule(Personnel, Economy, Governance, Treasures, kind) : kind == PersonnelReturns.Kind ? new PersonnelReturnRule(Personnel, Military)
        : kind == UnitStrategies.Kind ? new UnitStrategyRule(this) : kind == BurnStrategies.Kind ? new BurnRule(this)
        : kind == AlienateStrategies.Kind ? new AlienateRule(this)
        : kind == SeizeStrategies.Kind ? new SeizeRule(this)
        : kind == GossipStrategies.Kind ? new GossipRule(this)
        : kind == GiftDiplomacy.Kind ? new GiftRule(this)
        : kind == PrisonerReturnDiplomacy.Kind ? new PrisonerReturnRule(this)
        : kind == AttackRequestDiplomacy.Kind ? new AttackRequestRule(this)
        : kind == SurrenderDiplomacy.Kind ? new SurrenderRule(this) : null;
    internal virtual void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) { }
}

internal abstract class TaskRule
{
    internal virtual TaskResolutionPhase Phase => TaskResolutionPhase.DailyCommands;
    // Calendar-day offset from submission to this rule's resolution phase.
    internal abstract int DurationDays(PersistentTask task);
    internal virtual bool HasValidPayload(PersistentTask task) => task.Journey is null;
    internal virtual bool SupportsResolutionStep(int step) => step == 0;
    internal abstract bool Validate(WorldMutation world, PersistentTask task);
    internal virtual int? DecisionRecipient(WorldMutation world, PersistentTask task) => null;
    internal virtual bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) => response.Offer is null;
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
    internal void Prepare(WorldMutation world, List<DomainEvent> events, ResolutionStage? after = null)
    {
        var boundary = world.Clock.Boundary;
        List<ResolutionStage> stages = [ResolutionStage.TerminalCheck, ResolutionStage.XunEvents, ResolutionStage.XunUpdate];
        if (boundary.Month) stages.Add(ResolutionStage.MonthEvents);
        stages.Add(ResolutionStage.AppearanceAndIndependence);
        if (boundary.Quarter) stages.AddRange([ResolutionStage.QuarterPrelude, ResolutionStage.QuarterEvents, ResolutionStage.QuarterUpdate]);
        if (boundary.Year) stages.AddRange([ResolutionStage.AnnualAppearance, ResolutionStage.AnnualCleanup]);
        int start = after is { } completed ? stages.IndexOf(completed) + 1 : 0;
        if (after is not null && start == 0) throw new InvalidOperationException("Invalid preparation continuation.");
        foreach (var stage in stages.Skip(start))
            if (!Run(stage, world, events)) return;
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = 0 });
    }

    internal void AdvanceDay(WorldMutation world, List<DomainEvent> events) =>
        AdvanceDay(world, events, new(world.Clock.Year, world.Clock.Month, world.Clock.Xun));

    internal void AdvanceDay(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        ImmutableArray<Guid>? remainingTasks = null)
    {
        if (remainingTasks is null && !Run(ResolutionStage.BeforeDailyCommands, world, events)) return;
        ResolveTasks(world, events, xunStarted, remainingTasks: remainingTasks);
        if (world.Simulation.PendingDailyTask is not null) return;
        if (!CheckTerminal(world, events) || !Run(ResolutionStage.DailyCommands, world, events)) return;
        world.SetClock(world.Clock.NextDay());
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks
            .Select(t => t with { ProgressDays = checked(t.ProgressDays + 1) }).ToImmutableArray() });
        if (!Run(ResolutionStage.DailyMapUpdate, world, events)) return;
        FinishDay(world, events, xunStarted);
    }

    private void FinishDay(WorldMutation world, List<DomainEvent> events, GameClock xunStarted,
        ImmutableArray<Guid>? remainingTasks = null)
    {
        if (world.Clock.IsXunBoundary)
        {
            ResolveTasks(world, events, xunStarted, phase: TaskResolutionPhase.XunBoundary, remainingTasks: remainingTasks);
            if (world.Simulation.PendingDailyTask is not null || !CheckTerminal(world, events)) return;
        }
        // The later unit/building phase settles returns already due in the completed day,
        // including those created by that day's commands. Advancing the date is not another countdown tick.
        // A zero-distance return created by boundary diplomacy can also settle immediately.
        ResolveTasks(world, events, xunStarted, returningAfterDateAdvance: true);
        Run(ResolutionStage.UnitAndBuildingEvents, world, events);
    }

    private bool Run(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
    {
        world.MaintainLeadership(events);
        if (!CheckTerminal(world, events)) return false;
        if (stage == ResolutionStage.XunUpdate)
        {
            AttackRequests.UpdateXun(world, events);
            AllianceDiplomacy.UpdateXun(world, events);
            XunRecovery.Apply(world);
            PrisonerCycle.Apply(world, events);
        }
        if (stage == ResolutionStage.QuarterEvents) AllianceFormation.Begin(world, rules, events);
        if (stage == ResolutionStage.QuarterUpdate) PersonnelLoyaltyCycle.Apply(world, events);
        var rationAccounts = EconomicCycle.Apply(stage, world, rules.Economy, events);
        if (stage == ResolutionStage.BeforeDailyCommands)
        {
            MilitaryTactics.TickCooldowns(world);
            MilitaryConditions.Tick(world, events);
            MilitaryDailyCycle.Apply(world, rationAccounts, rules, events);
            BattleLearning.Daily(world, rules.Military, events);
        }
        if (stage == ResolutionStage.XunUpdate) WoundedRecovery.Apply(world, rules.Military, events);
        if (stage == ResolutionStage.XunUpdate) PrisonerEscapeCycle.Apply(world, rules.Personnel, rules.Economy, events);
        if (stage == ResolutionStage.DailyMapUpdate) { MilitaryPolicies.AdvanceDay(world, rules.Military, events); world.MaintainLeadership(events); }
        if (stage == ResolutionStage.DailyMapUpdate) { MilitaryMovement.AdvanceDay(world, rules.Military, rules.Governance, rules.Personnel, events); world.MaintainLeadership(events); }
        if (stage == ResolutionStage.DailyMapUpdate) { FacilityConstruction.AdvanceDay(world, rules, events); world.MaintainLeadership(events); }
        if (stage == ResolutionStage.DailyMapUpdate) { MilitaryCombat.AdvanceDay(world, rules, events); world.MaintainLeadership(events); }
        if (stage == ResolutionStage.DailyMapUpdate) { TacticSupport.AdvanceDay(world, rules, events); world.MaintainLeadership(events); }
        if (stage == ResolutionStage.DailyMapUpdate) { MilitaryPolicies.ResolveOrders(world, rules.Military, events); world.MaintainLeadership(events); }
        rules.ApplyStage(stage, world, events);
        AttackRequests.Cleanup(world, events);
        world.MaintainLeadership(events);
        events.Add(new StageCompletedEvent(world.Clock, stage));
        return CheckTerminal(world, events) && world.Simulation.PendingAlliance is null;
    }

    internal static bool CheckTerminal(WorldMutation world, List<DomainEvent> events)
    {
        if (world.Simulation.TerminalReason is not string reason) return true;
        foreach (var task in world.Simulation.Tasks.OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id))
        {
            ReinforcementTransport.Disband(world, task, "terminal", events);
            TaskCancellation.Apply(world, task, "terminal", events);
        }
        world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.Terminal, Tasks = [], PendingAlliance = null, PendingDailyTask = null });
        if (!events.OfType<SimulationEndedEvent>().Any()) events.Add(new SimulationEndedEvent(world.Clock, reason));
        return false;
    }

    internal static void CheckConquest(WorldMutation world, List<DomainEvent> events)
    {
        if (!events.OfType<ForceEliminatedEvent>().Any()) return;
        if (events.OfType<ForceEliminatedEvent>().Any(e => e.WasPlayerControlled))
            world.SetSimulation(world.Simulation with { TerminalReason = "player-defeated" });
        else if (world.Forces.Values.Count(f => !f.IsEliminated && !f.IsBarbarian) <= 1)
            world.SetSimulation(world.Simulation with { TerminalReason = "conquest" });
    }

    internal void Respond(WorldMutation world, RespondDailyTaskCommand response, List<DomainEvent> events)
    {
        var pending = world.Simulation.PendingDailyTask!;
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
            TaskCancellation.Apply(world, task, "invalid-target-or-executor", events);
            if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
        }
        world.MaintainLeadership(events);
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
                    world.Clock.IsXunBoundary && t.Due == world.Clock && t.Journey?.ReturnLeg?.CountdownDays == 0)
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
                TaskCancellation.Apply(world, task, "invalid-target-or-executor", events);
                if (task.Journey is not null) BattlePersonnel.ResumeReturn(world, task, rules, events);
            }
            world.MaintainLeadership(events);
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
        officer.SettlementId == task.OriginSettlementId && world.Settlements.TryGetValue(task.OriginSettlementId, out var target) &&
        target.OwnerId == task.ForceId && target.CanHostPersons && target.Durability > 0;
}
