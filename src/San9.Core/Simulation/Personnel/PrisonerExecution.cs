using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ExecutePrisonerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ChooseExecutionSuccessorCommand(Guid CommandId, int IssuerId, long Sequence, int RulerId, [property: JsonRequired] int? SuccessorId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ExecutionSuccessionDecision([property: JsonRequired] int ExecutingForceId,
    [property: JsonRequired] int RulerId, [property: JsonRequired] int RespondingForceId);
public sealed record ExecutionSuccessionPrompt(int ExecutingForceId, int RulerId, int RespondingForceId,
    ImmutableArray<int> CandidateIds);
public sealed record ExecutionSuccessionRequestedEvent(GameClock Clock, int ExecutingForceId, int RulerId, int RespondingForceId) : DomainEvent;
public sealed record PrisonerExecutedEvent(GameClock Clock, int ExecutingForceId, int OfficerId) : DomainEvent;

internal static class PrisonerExecution
{
    internal static bool DetainedBy(WorldMutation world, OfficerState person, int forceId) =>
        person.Status == OfficerStatus.Captive && person.OwnerId != forceId &&
        (person.SettlementId is int place && world.Settlements.TryGetValue(place, out var prison) &&
            prison.OwnerId == forceId && !prison.IsDestroyed && prison.CanHostPersons && prison.Durability > 0 ||
         person.UnitId is int unit && world.Units.TryGetValue(unit, out var captor) && captor.OwnerId == forceId);

    internal static ImmutableArray<int> Candidates(WorldMutation world, int forceId) => world.Officers.Values
        .Where(p => p.IsServing && p.OwnerId == forceId).OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray();

    internal static CommandRejection Start(WorldMutation world, ExecutePrisonerCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Officers.TryGetValue(command.OfficerId, out var prisoner)) return CommandRejection.UnknownPerson;
        if (!DetainedBy(world, prisoner, command.IssuerId)) return CommandRejection.PersonUnavailable;
        if (prisoner.OwnerId is int owner && world.Forces[owner].LeaderId == prisoner.Id &&
            world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) &&
            !Candidates(world, owner).IsEmpty &&
            (world.Settlements.Values.Any(s => s.OwnerId == owner && !s.IsDestroyed) || world.Units.Values.Any(u => u.OwnerId == owner)))
        {
            world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision,
                PendingExecutionSuccession = new(command.IssuerId, prisoner.Id, owner) });
            events.Add(new ExecutionSuccessionRequestedEvent(world.Clock, command.IssuerId, prisoner.Id, owner));
        }
        else Complete(world, command.IssuerId, prisoner.Id, null, rules, events);
        return CommandRejection.None;
    }

    internal static CommandRejection Respond(WorldMutation world, ChooseExecutionSuccessorCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.PendingExecutionSuccession is not { } pending || pending.RulerId != command.RulerId ||
            pending.RespondingForceId != command.IssuerId || command.SuccessorId is int chosen && !Candidates(world, command.IssuerId).Contains(chosen))
            return CommandRejection.InvalidDecision;
        world.SetSimulation(world.Simulation with { PendingExecutionSuccession = null, Boundary = SimulationBoundary.Stable });
        Complete(world, pending.ExecutingForceId, pending.RulerId, command.SuccessorId, rules, events);
        return CommandRejection.None;
    }

    private static void Complete(WorldMutation world, int executingForceId, int prisonerId, int? successorId,
        SimulationRules rules, List<DomainEvent> events)
    {
        var prisoner = world.Officers[prisonerId];
        if (prisoner.OwnerId is int owner && !world.Forces[owner].IsEliminated)
        {
            ForceRelations.Change(world, executingForceId, owner, -5 - (int)(world.NextRandom("personnel-execution-relations") % 3), events);
            foreach (var person in world.Officers.Values.Where(p => p.OwnerId == owner).OrderBy(p => p.Id).ToArray())
            {
                int loss = (prisoner.BloodlineId is not null && person.BloodlineId == prisoner.BloodlineId ? 5 : 0) +
                    (person.ParentId == prisoner.Id ? 5 : 0);
                if (loss > 0) RecruitmentRules.SetLoyalty(world, person.Id, executingForceId,
                    Math.Max(0, person.LoyaltyTo(executingForceId) - loss), "prisoner-executed", events);
            }
        }
        OfficerDeath.Resolve(world, prisonerId, OfficerDeathCause.Execution, rules, events, afterDailyCommands: false, successorId: successorId);
        events.Add(new PrisonerExecutedEvent(world.Clock, executingForceId, prisonerId));
        ResolutionPipeline.CheckTerminal(world, events);
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingExecutionSuccession is not { } pending) return;
        var sim = world.Simulation;
        if (!world.Clock.IsXunBoundary || sim.ActiveControl >= sim.Controls.Length ||
            sim.Controls[sim.ActiveControl].ForceId != pending.ExecutingForceId ||
            !world.Officers.TryGetValue(pending.RulerId, out var person) || person.OwnerId != pending.RespondingForceId ||
            !DetainedBy(world, person, pending.ExecutingForceId) ||
            !world.Forces.TryGetValue(pending.RespondingForceId, out var force) || force.LeaderId != person.Id || force.IsEliminated ||
            !sim.Controls.Any(c => c.ForceId == pending.RespondingForceId && c.Kind == ControllerKind.Player) ||
            Candidates(world, pending.RespondingForceId).IsEmpty ||
            !world.Settlements.Values.Any(s => s.OwnerId == pending.RespondingForceId && !s.IsDestroyed) &&
            !world.Units.Values.Any(u => u.OwnerId == pending.RespondingForceId))
            throw new ArgumentException("Invalid pending execution succession.");
    }
}
