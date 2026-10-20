using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BurnCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetSettlementId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record BurnMission(int TargetSettlementId);
public sealed record BurnResolvedEvent(GameClock Clock, int OfficerId, int TargetSettlementId, int Threshold,
    int Roll, bool Success, int DurabilityLoss, uint FoodLoss, int InjuryBefore, int InjuryAfter) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired]
    public BurnOccurrence Occurrence { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired] public StrategyAudience Audience { get; init; } = null!;
}
public sealed record BurnOccurrence(int ForceId, int TargetForceId, int Durability);

internal static class BuildingStrategyTargets
{
    internal static bool Eligible(WorldMutation world, int forceId, int origin, int targetId, SimulationRules rules)
    {
        if (rules.Military is not { } military || !world.Settlements.TryGetValue(targetId, out var target) ||
            target.IsDestroyed || target.Durability <= 0 || target.Facility?.BuilderUnitId is not null ||
            target.OwnerId is not int owner || !MilitaryDiplomacy.CanUseHostileStrategy(world, forceId, owner) ||
            world.Forces[owner].IsBarbarian ||
            MilitaryDiplomacy.Work(world, targetId, military) || !rules.Personnel.Places.ContainsKey(origin) ||
            !rules.Personnel.Places.ContainsKey(targetId) ||
            !rules.Personnel.RegionCities.TryGetValue(origin, out int sourceCity) ||
            !rules.Personnel.RegionCities.TryGetValue(targetId, out int targetCity)) return false;
        return sourceCity == targetCity || rules.Personnel.CityConnections.Any(c => c.FromCityId == sourceCity && c.ToCityId == targetCity);
    }
}

internal static class BurnStrategies
{
    internal const string Kind = "strategy-burn";

    internal static bool Eligible(WorldMutation world, int forceId, int origin, int targetId, SimulationRules rules)
        => BuildingStrategyTargets.Eligible(world, forceId, origin, targetId, rules);

    internal static CommandRejection Eligibility(WorldMutation world, int force, int officer, int target, SimulationRules rules)
    {
        if (!PersonnelMissions.CanStartMission(world, force, officer, rules.Military)) return CommandRejection.PersonUnavailable;
        return Eligible(world, force, world.Officers[officer].SettlementId!.Value, target, rules)
            ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    internal static int Threshold(WorldMutation world, int officer, int target) => StrategyMath.BurnThreshold(
        world.Abilities(world.Officers[officer]).War,
        LeadershipMaintenance.Commander(world, target) is { } commander ? world.Abilities(commander).Leadership : 0,
        world.Settlements[target].Durability);

    internal static CommandAdvice Advice(WorldMutation world, int force, int officer, int target, SimulationRules rules)
    {
        var rejection = Eligibility(world, force, officer, target, rules);
        return rejection == CommandRejection.None
            ? AdviserJudgment.Evaluate(world, force, ConditionalRandomPurpose.Burn, officer, target, Threshold(world, officer, target))
            : new(rejection, null, AdviserOpinion.Unavailable);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, BurnCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var plan = MissionPreparation.Prepare(world, command, rules, out var rejection);
        if (plan is null) return rejection;
        var actor = world.Officers[command.OfficerId];
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Burn,
            world.Clock, actor.Id, command.TargetSettlementId);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id,
            plan.Origin, world.Clock, plan.Arrival, 0, command.Sequence)
        {
            Journey = new(command.TargetSettlementId, null, roll)
                { Route = plan.Route, Burn = new(command.TargetSettlementId) }
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, List<DomainEvent> events)
    {
        var target = world.Settlements[task.Journey!.Burn!.TargetSettlementId];
        var actor = world.Officers[task.OfficerId];
        var audience = StrategyAudience.Capture(world, task.ForceId, target);
        int threshold = Threshold(world, actor.Id, target.Id);
        int roll = task.Journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        int damage = 0;
        uint food = 0;
        if (success)
        {
            int war = world.Abilities(actor).War;
            damage = StrategyMath.BurnDurabilityLoss(war, target.Durability, (int)(world.NextRandom("strategy-burn-durability") % 5));
            world.Replace(target with { Durability = target.Durability - damage });
            if (world.NextRandom("strategy-burn-food-gate") % 100 < 3)
            {
                var account = ResourceAccounts.Resolve(world, target.OwnerId!.Value, target.CorpsId);
                food = StrategyMath.BurnFoodLoss(war, target.Soldiers, target.Wounded,
                    ResourceAccounts.Balance(world, account, ResourceKind.Food), (int)(world.NextRandom("strategy-burn-food-amount") % 100));
                ResourceAccounts.Change(world, account, ResourceKind.Food, -(long)food, Kind, target.Id, events);
            }
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.War, "strategy-war-growth", events);
        }
        else
        {
            int injury = Math.Max(actor.Injury, (int)Math.Min(7, actor.Injury + ((long)roll - threshold) / 10));
            OfficerInjuries.Apply(world, actor with { Injury = injury }, events);
            ForceRelations.Change(world, task.ForceId, target.OwnerId!.Value,
                -1 - (int)(world.NextRandom("strategy-hostile-failure-relations") % 3), events);
        }
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new BurnResolvedEvent(world.Clock, actor.Id, target.Id, threshold, roll, success, damage, food,
            actor.Injury, world.Officers[actor.Id].Injury)
        { Occurrence = new(task.ForceId, target.OwnerId!.Value, target.Durability - damage), Audience = audience });
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class BurnRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null,
          UnitStrategy: null, Burn: { TargetSettlementId: >= 0 } burn, Seize: null, Gossip: null, Route: { FieldDestination: false } } journey &&
        journey.DestinationPlaceId == burn.TargetSettlementId && rules.Personnel.Places.TryGetValue(burn.TargetSettlementId, out var target) &&
        DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => task.Journey is { Route: { } route } journey &&
        rules.Personnel.Places.ContainsKey(task.OriginSettlementId) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) &&
        (!route.FieldDestination || rules.Military?.RegionAt(route.Destination) == journey.DestinationPlaceId)
        ? Math.Max(0, route.Days - 1) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        BurnStrategies.Eligible(world, task.ForceId, PersonnelLocations.TaskHome(world, task).Id, task.Journey!.Burn!.TargetSettlementId, rules);
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        BurnStrategies.Resolve(world, task, rules, events);
}
