using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BurnCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetSettlementId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record BurnMission(int TargetSettlementId);
public sealed record BurnResolvedEvent(GameClock Clock, int OfficerId, int TargetSettlementId, int Threshold,
    int Roll, bool Success, int DurabilityLoss, uint FoodLoss, int InjuryBefore, int InjuryAfter) : DomainEvent;

internal static class BuildingStrategyTargets
{
    internal static bool Eligible(WorldMutation world, int forceId, int origin, int targetId, SimulationRules rules)
    {
        if (rules.Military is not { } military || !world.Settlements.TryGetValue(targetId, out var target) ||
            target.IsDestroyed || target.Durability <= 0 || target.Facility?.BuilderUnitId is not null ||
            target.OwnerId is not int owner || !MilitaryDiplomacy.CanFight(world, forceId, owner) ||
            world.Forces[owner].IsBarbarian || world.Simulation.Alliance?.Protects(forceId, owner) == true ||
            !world.Relations.ContainsKey((Math.Min(forceId, owner), Math.Max(forceId, owner))) ||
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

    private static CommandRejection Eligibility(WorldMutation world, int force, int officer, int target, SimulationRules rules)
    {
        if (!PersonnelMissions.CanAct(world, force, officer)) return CommandRejection.PersonUnavailable;
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
            ? AdviserJudgment.Evaluate(world, force, Kind, officer, target, Threshold(world, officer, target))
            : new(rejection, null, AdviserOpinion.Unavailable);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, BurnCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.TargetSettlementId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        var position = rules.Personnel.Places[command.TargetSettlementId].Position;
        int offset = Math.Max(0, PersonnelRulebook.JourneyDays(rules.Personnel.Places[origin].Position, position) - 1);
        var due = world.Clock;
        for (int day = 0; day < offset; day++) due = due.NextDay();
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Burn,
            world.Clock, actor.Id, command.TargetSettlementId);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id,
            origin, world.Clock, due, 0, command.Sequence)
        {
            Journey = new(command.TargetSettlementId, null, roll)
                { DestinationPosition = position, Burn = new(command.TargetSettlementId) }
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
            if (actor.BaseAbilities.War < 100 && world.NextRandom("strategy-war-growth") % 100 < 3)
            {
                world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { War = actor.BaseAbilities.War + 1 } });
                events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "war", actor.BaseAbilities.War, actor.BaseAbilities.War + 1));
            }
        }
        else
        {
            int injury = Math.Max(actor.Injury, (int)Math.Min(7, actor.Injury + ((long)roll - threshold) / 10));
            world.Replace(actor with { Injury = injury });
            ForceRelations.Change(world, task.ForceId, target.OwnerId!.Value,
                -1 - (int)(world.NextRandom("strategy-hostile-failure-relations") % 3), events);
        }
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new BurnResolvedEvent(world.Clock, actor.Id, target.Id, threshold, roll, success, damage, food,
            actor.Injury, world.Officers[actor.Id].Injury));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class BurnRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
          UnitStrategy: null, Burn: { TargetSettlementId: >= 0 } burn, Seize: null, Gossip: null, DestinationPosition: { } position } journey &&
        journey.DestinationPlaceId == burn.TargetSettlementId && rules.Personnel.Places.TryGetValue(burn.TargetSettlementId, out var target) &&
        target.Position == position && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => task.Journey is { DestinationPosition: { } position } &&
        rules.Personnel.Places.TryGetValue(task.OriginSettlementId, out var origin)
        ? Math.Max(0, PersonnelRulebook.JourneyDays(origin.Position, position) - 1) : -1;
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        BurnStrategies.Eligible(world, task.ForceId, task.OriginSettlementId, task.Journey!.Burn!.TargetSettlementId, rules);
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        BurnStrategies.Resolve(world, task, rules, events);
}
