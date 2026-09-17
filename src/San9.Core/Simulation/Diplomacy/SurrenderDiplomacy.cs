using System.Numerics;
using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DemandSurrenderCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    int TargetForceId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SurrenderMission([property: JsonRequired] int TargetForceId);
public sealed record SurrenderResolvedEvent(GameClock Clock, Guid TaskId, int RequesterForceId, int TargetForceId,
    bool Accepted, int Threshold, int Roll) : DomainEvent;

internal static class SurrenderDiplomacy
{
    internal const string Kind = "diplomacy-surrender";
    internal static bool Target(WorldMutation world, int source, int target) => GiftDiplomacy.Target(world, source, target) &&
        !world.Forces[target].IsBarbarian && world.Simulation.Alliance?.Protects(source, target) != true &&
        !AttackRequests.Protects(world, source, target);

    internal static CommandRejection Eligibility(WorldMutation world, int source, int actor, int target, SimulationRules rules)
    {
        if (!Target(world, source, target)) return CommandRejection.InvalidTask;
        if (!PersonnelMissions.CanAct(world, source, actor)) return CommandRejection.PersonUnavailable;
        var origin = world.Settlements[world.Officers[actor].SettlementId!.Value];
        if (ResourceAccounts.Resolve(world, source, origin.CorpsId).Kind != ResourceOwnerKind.Force) return CommandRejection.InvalidCorps;
        return rules.Personnel.Places.ContainsKey(origin.Id) && GiftDiplomacy.Destination(world, target, rules, out _, out _)
            ? CommandRejection.None : CommandRejection.NoDestination;
    }

    internal static long Strength(WorldMutation world, int force) =>
        world.Settlements.Values.Where(s => s.OwnerId == force && !s.IsDestroyed).Sum(s => (long)s.Soldiers + s.Wounded) +
        world.Units.Values.Where(u => u.OwnerId == force).Sum(u => (long)u.Soldiers + u.Wounded) +
        world.Simulation.Tasks.Where(t => t.ForceId == force).Sum(t => (long)(t.Reinforcements?.Soldiers ?? 0));

    internal static bool Neighbors(WorldMutation world, int first, int second, SimulationRules rules)
    {
        if (rules.Military is not { } map) return false;
        var owners = world.Settlements.Values.Where(s => !s.IsDestroyed && s.OwnerId is not null && map.TryPlace(s, out _))
            .Select(s => (Region: map.RegionAt(map.Place(s).Position), Owner: s.OwnerId!.Value)).Where(p => p.Region is not null)
            .ToLookup(p => p.Region!.Value, p => p.Owner);
        return map.TerritoryConnections(world.Settlements.Values).Any(c => owners[c.FromRegionId].Contains(first) && owners[c.ToRegionId].Contains(second));
    }

    internal static int Threshold(WorldMutation world, int source, int actor, int target, SimulationRules rules)
    {
        var ruler = world.Officers[world.Forces[target].LeaderId!.Value];
        if (!ruler.AutomaticSurrenderAllowed || !Neighbors(world, source, target, rules)) return 0;
        long ratio = Strength(world, source) / Math.Max(20000, Strength(world, target));
        if (ratio < 5) return 0;
        int cities = world.Settlements.Values.Count(s => s.OwnerId == target && s.Kind == SettlementKind.City && !s.IsDestroyed);
        long term = (Math.Clamp(world.Forces[source].Trust, 0, 1000) - Math.Clamp(world.Forces[target].Trust, 0, 1000)) / 40 +
            ratio - 10L * cities - Math.Max(0, 5 * ruler.Ambition - 40) + 10;
        var value = (BigInteger)EconomyMath.ExecutedAbility(world.Abilities(world.Officers[actor]).Politics) * term / 60;
        return (int)BigInteger.Clamp(value, int.MinValue, int.MaxValue);
    }

    internal static CommandAdvice Advice(WorldMutation world, int source, int actor, int target, SimulationRules rules)
    {
        var rejection = Eligibility(world, source, actor, target, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        return PrisonerReturnDiplomacy.Player(world, target) ? new(CommandRejection.None, null, AdviserOpinion.Unavailable) :
            AdviserJudgment.Evaluate(world, source, ConditionalRandomPurpose.Surrender, actor, target, Threshold(world, source, actor, target, rules));
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, DemandSurrenderCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.TargetForceId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        GiftDiplomacy.Destination(world, command.TargetForceId, rules, out int destination, out var position);
        var route = PersonnelLocations.Route(world, rules.Personnel, origin, destination, position);
        int days = route.XunRoundedDays;
        var due = world.Clock;
        for (int i = 0; i < days; i++) due = due.NextDay();
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, origin,
            world.Clock, due, 0, command.Sequence)
        {
            Journey = new(destination, null, ConditionalRandom.Percentile(world.Simulation.InitialSeed,
                ConditionalRandomPurpose.Surrender, world.Clock, actor.Id, command.TargetForceId)) { Route = route },
            Surrender = new(command.TargetForceId)
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static void Finish(WorldMutation world, PersistentTask task, bool accept, SimulationRules rules, List<DomainEvent> events)
    {
        int target = task.Surrender!.TargetForceId;
        int threshold = Threshold(world, task.ForceId, task.OfficerId, target, rules);
        if (accept)
        {
            SurrenderMerge.Apply(world, task, target, rules, events);
            DiplomacySettlement.Grow(world, task.OfficerId, events);
        }
        else ForceRelations.Set(world, task.ForceId, target,
            DiplomacyMath.SurrenderRejectedRelation(MilitaryDiplomacy.Relation(world, task.ForceId, target)), events);
        events.Add(new SurrenderResolvedEvent(world.Clock, task.Id, task.ForceId, target, accept, threshold, task.Journey!.SuccessRoll!.Value));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
        ResolutionPipeline.CheckConquest(world, events);
    }
}

internal sealed class SurrenderRule(SimulationRules rules) : TaskRule
{
    internal override TaskResolutionPhase Phase => TaskResolutionPhase.XunBoundary;
    internal override bool HasValidPayload(PersistentTask task) => task.Gift is null && task.PrisonerReturn is null && task.AttackRequest is null &&
        task.Reinforcements is null && task.Surrender is { TargetForceId: >= 0 } mission && mission.TargetForceId != task.ForceId && task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null } && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => DiplomacyJourney.Duration(task, rules);
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        SurrenderDiplomacy.Target(world, task.ForceId, task.Surrender!.TargetForceId);
    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) =>
        PrisonerReturnDiplomacy.Player(world, task.Surrender!.TargetForceId) ? task.Surrender.TargetForceId : null;
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        SurrenderDiplomacy.Finish(world, task, task.Journey!.SuccessRoll < SurrenderDiplomacy.Threshold(world, task.ForceId,
            task.OfficerId, task.Surrender!.TargetForceId, rules), rules, events);
    internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events) =>
        SurrenderDiplomacy.Finish(world, task, accept, rules, events);
}
