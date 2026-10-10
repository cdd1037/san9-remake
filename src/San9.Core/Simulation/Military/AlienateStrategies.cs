using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record AlienateCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetOfficerId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record AlienateResolvedEvent(GameClock Clock, int OfficerId, int TargetOfficerId, int Threshold,
    int Roll, bool Success, int LoyaltyLoss, int RelationForceId) : DomainEvent;

internal static class AlienateStrategies
{
    internal const string Kind = "strategy-alienate";

    internal static bool EligibleTarget(WorldMutation world, int forceId, int targetId, SimulationRules rules)
    {
        if (!world.Officers.TryGetValue(targetId, out var target) || !target.IsServing ||
            target.Status == OfficerStatus.Ruler || target.OwnerId is not int owner || owner == forceId ||
            target.Loyalty <= 0 || world.Forces[owner].IsBarbarian ||
            !MilitaryDiplomacy.CanUseHostileStrategy(world, forceId, owner)) return false;
        return TryDestination(world, target, rules, out _, out _);
    }

    internal static CommandRejection Eligibility(WorldMutation world, int forceId, int officerId, int targetId,
        SimulationRules rules)
    {
        if (!PersonnelMissions.CanAct(world, forceId, officerId)) return CommandRejection.PersonUnavailable;
        if (!EligibleTarget(world, forceId, targetId, rules)) return CommandRejection.InvalidTask;
        int origin = world.Officers[officerId].SettlementId!.Value;
        return rules.Personnel.Places.ContainsKey(origin)
            ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    internal static bool TryDestination(WorldMutation world, OfficerState target, SimulationRules rules,
        out int placeId, out WorldPosition? position)
    {
        position = null;
        if (target.SettlementId is int settlement && world.Settlements.TryGetValue(settlement, out var place) &&
            place.OwnerId == target.OwnerId && place.CanHostPersons && place.Durability > 0 &&
            rules.Personnel.Places.ContainsKey(settlement))
        {
            placeId = settlement;
            return true;
        }
        if (target.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit) && unit.OwnerId == target.OwnerId &&
            rules.Military?.RegionAt(unit.Position) is int region && rules.Personnel.Places.ContainsKey(region))
        {
            placeId = region;
            position = unit.Position;
            return true;
        }
        placeId = -1;
        return false;
    }

    internal static int Threshold(WorldMutation world, int officerId, int targetId)
    {
        var target = world.Officers[targetId];
        int distance = target.OwnerId is int owner && world.Forces[owner].LeaderId is int rulerId &&
            world.Officers.TryGetValue(rulerId, out var ruler) && ruler.Status != OfficerStatus.Dead
            ? PersonnelRulebook.AffinityDistance(target, ruler) : 50;
        return StrategyMath.AlienateThreshold(world.Abilities(world.Officers[officerId]).Intelligence,
            world.Abilities(target).Intelligence, target.Righteousness, distance);
    }

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int officerId, int targetId,
        SimulationRules rules)
    {
        var rejection = Eligibility(world, forceId, officerId, targetId, rules);
        return rejection == CommandRejection.None
            ? AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Alienate, officerId, targetId, Threshold(world, officerId, targetId))
            : new(rejection, null, AdviserOpinion.Unavailable);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, AlienateCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var plan = MissionPreparation.Prepare(world, command, rules, out var rejection);
        if (plan is null) return rejection;
        var actor = world.Officers[command.OfficerId];
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Alienate,
            world.Clock, actor.Id, command.TargetOfficerId);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id,
            plan.Origin, world.Clock, plan.Arrival, 0, command.Sequence)
        {
            Journey = new(plan.Destination, command.TargetOfficerId, roll) { Route = plan.Route }
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, PersonnelRulebook places,
        List<DomainEvent> events)
    {
        var actor = world.Officers[task.OfficerId];
        var target = world.Officers[task.Journey!.TargetOfficerId!.Value];
        int threshold = Threshold(world, actor.Id, target.Id);
        int roll = task.Journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        int loyaltyLoss = 0;
        int relationForce = task.ForceId;
        if (success)
        {
            int targetForce = target.OwnerId!.Value;
            relationForce = world.Forces.Values
                .Where(force => !force.IsEliminated && !force.IsBarbarian && force.Id != task.ForceId &&
                    force.Id != targetForce)
                .OrderByDescending(force => ForceRelations.Get(world, force.Id, targetForce).Value)
                .ThenBy(force => force.Id)
                .Select(force => force.Id)
                .FirstOrDefault(task.ForceId);
            loyaltyLoss = StrategyMath.AlienateLoyaltyLoss(target.Righteousness,
                (int)(world.NextRandom("strategy-alienate-loyalty") % 3));
            RecruitmentRules.SetLoyalty(world, target.Id, target.OwnerId!.Value,
                Math.Max(0, target.Loyalty - loyaltyLoss), Kind, events);
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Intelligence, "strategy-intelligence-growth", events);
        }
        ForceRelations.Change(world, relationForce, target.OwnerId!.Value,
            -1 - (int)(world.NextRandom("strategy-alienate-relations") % 3), events);
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new AlienateResolvedEvent(world.Clock, actor.Id, target.Id, threshold, roll, success, loyaltyLoss,
            relationForce));
        PersonnelReturns.Start(world, task, places, events, true);
    }
}

internal sealed class AlienateRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { DestinationPlaceId: >= 0, TargetOfficerId: >= 0, SuccessRoll: >= 0 and < 100,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null, ReturnLeg: null } &&
        DurationDays(task) >= 0;

    internal override int DurationDays(PersistentTask task) => task.Journey is { Route: { } route } journey &&
        rules.Personnel.Places.ContainsKey(task.OriginSettlementId) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) &&
        (!route.FieldDestination || rules.Military?.RegionAt(route.Destination) == journey.DestinationPlaceId)
        ? Math.Max(0, route.Days - 1) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        AlienateStrategies.EligibleTarget(world, task.ForceId, task.Journey!.TargetOfficerId!.Value, rules);

    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        AlienateStrategies.Resolve(world, task, rules.Personnel, events);
}
