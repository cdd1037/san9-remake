using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ChooseTacticStudentCommand(Guid CommandId, int IssuerId, long Sequence, int CityId, int? OfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record TacticStudyDecision([property: JsonRequired] int CityId, [property: JsonRequired] int RespondingForceId);
public sealed record TacticStudyPrompt(int CityId, int ForceId, int TacticId, ImmutableArray<int> CandidateIds);
public sealed record TacticStudyOfferedEvent(GameClock Clock, int CityId, int ForceId, int TacticId) : DomainEvent;
public sealed record TacticStudyDecidedEvent(GameClock Clock, int CityId, int ForceId, int? OfficerId) : DomainEvent;

internal static class TacticStudy
{
    internal const string Kind = "tactic-study";

    internal static bool PlayerCity(WorldMutation world, int cityId) =>
        world.Settlements.TryGetValue(cityId, out var city) && city.Kind == SettlementKind.City &&
        city.CanHostPersons && !city.IsDestroyed && city.Durability > 0 && city.OwnerId is int owner &&
        world.Simulation.Controls.Any(c => c.ForceId == owner && c.Kind == ControllerKind.Player) &&
        (city.CorpsId is not int corps || world.Corps[corps].ControlMode == CorpsControlMode.Direct);

    internal static ImmutableArray<int> Candidates(WorldMutation world, SimulationRules rules, int cityId)
    {
        if (!PlayerCity(world, cityId) || !rules.Personnel.TacticCourses.TryGetValue(cityId, out int tacticId) ||
            rules.Military is null || !rules.Military.Tactics.TryGetValue(tacticId, out var tactic) || tactic.Category is not int category ||
            world.Simulation.Tasks.Any(t => t.Kind == Kind && t.OriginSettlementId == cityId)) return [];
        int owner = world.Settlements[cityId].OwnerId!.Value;
        return world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.SettlementId == cityId && p.UnitId is null &&
            !p.LearnedTactics.Contains(tacticId) && p.TacticProficiencies[category] >= tactic.TrainingRequirement / 2 &&
            !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id)).OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray();
    }

    internal static TacticStudyPrompt? Prompt(WorldMutation world, SimulationRules rules) =>
        world.Simulation.PendingTacticStudy is { } pending ? new(pending.CityId, pending.RespondingForceId,
            rules.Personnel.TacticCourses[pending.CityId], Candidates(world, rules, pending.CityId)) : null;

    internal static void Offer(WorldMutation world, SimulationRules rules, List<DomainEvent> events, int? afterCity = null)
    {
        if (world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        foreach (int city in rules.Personnel.TacticCourses.Keys.Order().Where(id => afterCity is null || id > afterCity))
        {
            if (Candidates(world, rules, city).IsEmpty) continue;
            int force = world.Settlements[city].OwnerId!.Value;
            world.SetSimulation(world.Simulation with { Boundary = SimulationBoundary.AwaitingDecision,
                PendingTacticStudy = new(city, force) });
            events.Add(new TacticStudyOfferedEvent(world.Clock, city, force, rules.Personnel.TacticCourses[city]));
            return;
        }
    }

    internal static CommandRejection Respond(WorldMutation world, ChooseTacticStudentCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        if (world.Simulation.PendingTacticStudy is not { } pending || pending.CityId != command.CityId ||
            pending.RespondingForceId != command.IssuerId ||
            command.OfficerId is int candidate && !Candidates(world, rules, pending.CityId).Contains(candidate))
            return CommandRejection.InvalidDecision;
        if (command.OfficerId is int officer)
        {
            var due = world.Clock;
            for (int day = 0; day < 90; day++) due = due.NextDay();
            var position = PersonnelLocations.Position(world, rules.Personnel, pending.CityId);
            var task = new PersistentTask(command.CommandId, Kind,
                world.Simulation.Controls.Single(c => c.ForceId == command.IssuerId).ControllerId,
                command.IssuerId, officer, pending.CityId, world.Clock, due, 0, command.Sequence)
            {
                StudyTacticId = rules.Personnel.TacticCourses[pending.CityId],
                Journey = new(pending.CityId, null, null) { Route = new(position, position, false) }
            };
            world.Replace(world.Officers[officer] with { Acted = true, IsCommander = false });
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
            events.Add(new TaskStartedEvent(task));
        }
        events.Add(new TacticStudyDecidedEvent(world.Clock, pending.CityId, command.IssuerId, command.OfficerId));
        world.SetSimulation(world.Simulation with { PendingTacticStudy = null });
        Offer(world, rules, events, pending.CityId);
        return CommandRejection.None;
    }

    internal static void CompleteQuarter(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        var rule = new TacticStudyRule(rules);
        foreach (var task in world.Simulation.Tasks.Where(t => t.Kind == Kind && t.Due.DayNumber <= world.Clock.DayNumber)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            if (!ResolutionPipeline.TaskReferencesValid(world, task) || !rule.Validate(world, task))
            {
                TaskCancellation.Apply(world, task, "study-invalidated", events);
                BattlePersonnel.ResumeReturn(world, task, rules, events);
                continue;
            }
            world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
            rule.Complete(world, task, events);
            events.Add(new TaskCompletedEvent(task.Id, world.Clock));
        }
    }

    internal static void ValidatePending(WorldMutation world)
    {
        if (world.Simulation.PendingTacticStudy is not { } pending) return;
        if (!world.Clock.Boundary.Quarter || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30 ||
            world.Simulation.ActiveControl != world.Simulation.Controls.Length || !PlayerCity(world, pending.CityId) ||
            world.Settlements[pending.CityId].OwnerId != pending.RespondingForceId)
            throw new ArgumentException("Invalid tactic study decision.");
    }
}

internal sealed class TacticStudyRule(SimulationRules rules) : TaskRule
{
    internal override int DurationDays(PersistentTask task) => 90;
    internal override bool HasValidPayload(PersistentTask task) => task.Kind == TacticStudy.Kind && task.Started.Boundary.Quarter &&
        task.StudyTacticId is int tactic && rules.Personnel.TacticCourses.TryGetValue(task.OriginSettlementId, out int course) && tactic == course &&
        rules.Military?.Tactics.ContainsKey(tactic) == true && task.Journey is { TargetOfficerId: null, SuccessRoll: null, ReturnLeg: null,
            UnitStrategy: null, Burn: null, Seize: null, Gossip: null, Route.FieldDestination: false } journey &&
        journey.DestinationPlaceId == task.OriginSettlementId && journey.Route.Origin == journey.Route.Destination &&
        journey.Route.Origin == rules.Personnel.Places[task.OriginSettlementId].Position &&
        task.Gift is null && task.PrisonerReturn is null && task.AttackRequest is null && task.Surrender is null && task.Reinforcements is null;
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task);
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
    {
        if (TacticStudy.PlayerCity(world, task.OriginSettlementId))
            OfficerTacticGrowth.Grant(world, task.OfficerId, rules.Military!.Tactics[task.StudyTacticId!.Value], events);
    }
}
