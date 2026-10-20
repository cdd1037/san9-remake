using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum UnitStrategyKind { FalseReport, Disorder, Inspire, Reinforce }
public sealed record UnitStrategyCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    UnitStrategyKind Strategy, int TargetUnitId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record UnitStrategyMission(UnitStrategyKind Strategy, int TargetUnitId);
public sealed record UnitStrategyTarget(WorldPosition Position, int ForceId, int? CommanderId, int? ServicePlaceId,
    UnitCondition Condition, bool DefensiveFormation, bool HasCounterConfusion);
public sealed record UnitStrategyResolvedEvent(GameClock Clock, int OfficerId, int TargetUnitId,
    UnitStrategyKind Strategy, int Threshold, int Roll, bool Success,
    [property: System.Text.Json.Serialization.JsonRequired] UnitStrategyTarget Target) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public StrategyAudience Audience { get; init; } = null!;
    [System.Text.Json.Serialization.JsonRequired] public int PreviousMorale { get; init; }
    public uint RequestedSoldiers { get; init; }
    public uint DeliveredSoldiers { get; init; }
}

internal static class UnitStrategies
{
    internal const string Kind = "unit-strategy";

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int officerId, UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        var rejection = SubmissionEligibility(world, forceId, officerId, strategy, targetId, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        return AdviserJudgment.Evaluate(world, forceId, Purpose(strategy), officerId, targetId,
            Threshold(world, officerId, new(strategy, targetId), rules));
    }

    private static ConditionalRandomPurpose Purpose(UnitStrategyKind strategy) => strategy switch
    {
        UnitStrategyKind.FalseReport => ConditionalRandomPurpose.FalseReport,
        UnitStrategyKind.Disorder => ConditionalRandomPurpose.Disorder,
        UnitStrategyKind.Inspire => ConditionalRandomPurpose.Inspire,
        UnitStrategyKind.Reinforce => ConditionalRandomPurpose.Reinforcement,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    internal static CommandRejection SubmissionEligibility(WorldMutation world, int forceId, int officerId,
        UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        if (!PersonnelMissions.CanStartMission(world, forceId, officerId, rules.Military)) return CommandRejection.PersonUnavailable;
        if (!Eligible(world, forceId, strategy, targetId, rules)) return CommandRejection.InvalidTask;
        int origin = world.Officers[officerId].SettlementId!.Value;
        if (strategy == UnitStrategyKind.Reinforce &&
            (world.Settlements[origin].Soldiers < 3000 || !InReinforcementRange(world, origin, targetId, rules) ||
             Demobilization.City(world, origin, rules.Personnel) is null ||
             world.Units[targetId].Soldiers >= rules.Military!.Capacity(world.Units[targetId])))
            return CommandRejection.InvalidTask;
        return rules.Personnel.Places.ContainsKey(origin) && rules.Military!.RegionAt(world.Units[targetId].Position) is int region &&
            rules.Personnel.Places.ContainsKey(region) ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    private static bool InReinforcementRange(WorldMutation world, int origin, int targetId, SimulationRules rules)
    {
        if (rules.Military!.RegionAt(world.Units[targetId].Position) is not int targetRegion ||
            !rules.Personnel.RegionCities.TryGetValue(origin, out int sourceCity) ||
            !rules.Personnel.RegionCities.TryGetValue(targetRegion, out int targetCity)) return false;
        return sourceCity == targetCity ||
            rules.Personnel.CityConnections.Any(c => c.FromCityId == sourceCity && c.ToCityId == targetCity);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, UnitStrategyCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var plan = MissionPreparation.Prepare(world, command, rules, out var rejection);
        if (plan is null) return rejection;
        var actor = world.Officers[command.OfficerId];
        var target = world.Units[command.TargetUnitId];
        var purpose = Purpose(command.Strategy);
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, purpose, world.Clock, actor.Id, target.Id);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, plan.Origin,
            world.Clock, plan.Arrival, 0, command.Sequence)
        {
            Journey = new(plan.Destination, null, roll)
            {
                Route = plan.Route,
                UnitStrategy = new(command.Strategy, target.Id)
            },
            Reinforcements = command.Strategy == UnitStrategyKind.Reinforce
                ? new(3000, world.Settlements[plan.Origin].Morale, Demobilization.City(world, plan.Origin, rules.Personnel)!.Value)
                : null
        };
        if (command.Strategy == UnitStrategyKind.Reinforce)
        {
            var source = world.Settlements[plan.Origin];
            world.Replace(source with { Soldiers = source.Soldiers - 3000 });
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Settlement, source.Id), ResourceKind.Soldiers,
                Kind, target.Id, -3000, -3000, events);
        }
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static OfficerState[] Members(WorldMutation world, UnitState target) => world.ServingUnitMembers(target.Id)
        .Where(p => p.OwnerId == target.OwnerId).OrderBy(p => p.Id).ToArray();

    private static bool Knows(WorldMutation world, int force, IEnumerable<OfficerState> members, TacticEffect effect, SimulationRules rules) =>
        rules.Military!.Tactics.Values.Where(t => t.Effect == effect).Any(t =>
            MilitaryTactics.HasTactic(world, force, members.Select(p => p.Id).ToArray(), t.Id, rules.Treasures));

    private static UnitStrategyTarget CaptureTarget(WorldMutation world, UnitState target, SimulationRules rules) =>
        new(target.Position, target.OwnerId, target.CommanderId, target.ServicePlaceId, target.Condition,
            rules.Military!.Formation(target).HalvesHostileStrategyChance,
            Knows(world, target.OwnerId, Members(world, target), TacticEffect.CounterConfusion, rules));

    internal static bool Eligible(WorldMutation world, int force, UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        if (!Enum.IsDefined(strategy) || rules.Military is not { } military || !world.Units.TryGetValue(targetId, out var target) ||
            target.IsPersonnel || target.IsEmbedded || target.FormationId is not int formationId ||
            !military.Formations.TryGetValue(formationId, out var formation) || Members(world, target).Length == 0) return false;
        if (strategy == UnitStrategyKind.Inspire)
            return target.OwnerId == force && target.Condition != UnitCondition.HighSpirits &&
                (target.Condition != UnitCondition.Normal || target.Morale < 100);
        if (strategy == UnitStrategyKind.Reinforce)
            return target.OwnerId == force;
        if (!MilitaryDiplomacy.CanUseHostileStrategy(world, force, target.OwnerId)) return false;
        return strategy != UnitStrategyKind.Disorder || !formation.ImmuneToDisorder;
    }

    internal static int Threshold(WorldMutation world, int actorId, UnitStrategyMission mission, SimulationRules rules)
    {
        var target = world.Units[mission.TargetUnitId];
        var members = Members(world, target);
        if (mission.Strategy == UnitStrategyKind.Reinforce)
            return target.Soldiers >= rules.Military!.Capacity(target)
                ? 0 : StrategyMath.ReinforcementThreshold(world.Abilities(world.Officers[actorId]).Leadership);
        int intelligence = world.Abilities(world.Officers[actorId]).Intelligence;
        if (mission.Strategy == UnitStrategyKind.Inspire)
            return StrategyMath.InspireThreshold(intelligence, Knows(world, world.Officers[actorId].OwnerId!.Value,
                [world.Officers[actorId]], TacticEffect.Inspire, rules));
        if (Knows(world, target.OwnerId, members, TacticEffect.CounterConfusion, rules) ||
            (mission.Strategy == UnitStrategyKind.FalseReport ? target.Condition is not (UnitCondition.Normal or UnitCondition.NoFormation)
                : target.Condition != UnitCondition.Normal)) return 0;
        bool defensive = rules.Military!.Formation(target).HalvesHostileStrategyChance;
        return mission.Strategy == UnitStrategyKind.FalseReport
            ? StrategyMath.FalseReportThreshold(intelligence, members.Max(p => world.Abilities(p).Intelligence), defensive)
            : StrategyMath.DisorderThreshold(intelligence, members.Max(p => world.Abilities(p).Leadership), defensive);
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, List<DomainEvent> events)
    {
        var mission = task.Journey!.UnitStrategy!;
        var target = world.Units[mission.TargetUnitId];
        var occurrence = CaptureTarget(world, target, rules);
        int threshold = Threshold(world, task.OfficerId, mission, rules);
        int roll = task.Journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        var members = Members(world, target);
        if (mission.Strategy == UnitStrategyKind.Reinforce)
        {
            ResolveReinforcement(world, task, rules, events, target, threshold, roll, success);
            return;
        }
        if (success)
        {
            int days;
            UnitCondition condition;
            int morale = target.Morale;
            if (mission.Strategy == UnitStrategyKind.FalseReport)
            {
                condition = UnitCondition.Disguised;
                days = 5 + (int)(world.NextRandom("strategy-false-report-duration") % 3);
            }
            else if (mission.Strategy == UnitStrategyKind.Disorder)
            {
                condition = UnitCondition.NoFormation;
                long ability = EconomyMath.ExecutedAbility(members.Max(p => world.Abilities(p).Intelligence));
                long root = 0, upper = Math.Min(ability, int.MaxValue) + 1;
                while (root + 1 < upper)
                {
                    long middle = root + (upper - root) / 2;
                    if (middle <= ability / middle) root = middle;
                    else upper = middle;
                }
                days = (int)Math.Max(0, 20 - root);
                morale = Math.Max(1, morale - 1 - (int)(world.NextRandom("strategy-disorder-morale") % 3));
            }
            else
            {
                condition = UnitCondition.HighSpirits;
                var commander = target.CommanderId is int id ? world.Officers[id] : members[0];
                days = 3 + world.Abilities(commander).Leadership / 10;
                morale = Math.Min(100, morale + 20);
                foreach (var member in members) world.Replace(member with { TacticCooldown = 0 });
            }
            days = Math.Max(target.ConditionDaysRemaining, days);
            if (condition == UnitCondition.Disguised) FacilityConstruction.Cancel(world, target.Id, events);
            world.Replace(target with { Condition = condition, ConditionDaysRemaining = days, Morale = morale,
                HighSpiritsMoralePenaltyPending = condition == UnitCondition.HighSpirits,
                Order = condition == UnitCondition.Disguised ? null : target.Order,
                AwaitingPostAction = condition == UnitCondition.Disguised
                    ? rules.Military!.Purpose(target.FormationId!.Value) == UnitPurpose.Combat
                    : target.AwaitingPostAction,
                EngagementTargetId = condition == UnitCondition.Disguised ? null : target.EngagementTargetId });
            if (condition == UnitCondition.Disguised)
            {
                UnitOrderChanges.Report(world, target.Id, null, "false-report", events);
                if (target.EngagementTargetId is not null) events.Add(new UnitEngagementChangedEvent(world.Clock, target.Id, null, "false-report"));
            }
            events.Add(new BattleConditionChangedEvent(world.Clock, target.Id, false, condition, days)
            { SourceForceId = task.ForceId, Target = BattleObjectContext.Capture(target) });
            BattleLearning.Passive(world, members, TacticEffect.CounterConfusion, rules.Military!, events);
            var actor = world.Officers[task.OfficerId];
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Intelligence, "strategy-intelligence-growth", events);
        }
        else if (mission.Strategy == UnitStrategyKind.Inspire)
            world.Replace(target with { Morale = Math.Max(1, target.Morale - 1 - (int)(world.NextRandom("strategy-inspire-failure-morale") % 5)) });
        else ForceRelations.Change(world, task.ForceId, target.OwnerId,
            -1 - (int)(world.NextRandom("strategy-hostile-failure-relations") % 3), events);
        OfficerTacticGrowth.Increase(world, task.OfficerId, 7, 2, events);
        OfficerTacticGrowth.Increase(world, task.OfficerId, 8, 2, events);
        events.Add(new UnitStrategyResolvedEvent(world.Clock, task.OfficerId, target.Id, mission.Strategy, threshold, roll, success, occurrence)
        { Audience = StrategyAudience.Capture(world, task.ForceId, world.Units[target.Id]), PreviousMorale = target.Morale });
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }

    private static void ResolveReinforcement(WorldMutation world, PersistentTask task, SimulationRules rules,
        List<DomainEvent> events, UnitState target, int threshold, int roll, bool success)
    {
        var actor = world.Officers[task.OfficerId];
        var occurrence = CaptureTarget(world, target, rules);
        uint requested = success ? 3000u : StrategyMath.FailedReinforcement(world.Abilities(actor).Leadership);
        uint capacity = rules.Military!.Capacity(target);
        var troops = task.Reinforcements!;
        uint delivered = Math.Min(troops.Soldiers, Math.Min(requested, capacity - target.Soldiers));
        int morale = ReinforcementTransport.MixMorale(target.Soldiers, target.Morale, delivered, troops.Morale);
        UnitCondition condition = success && target.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits)
            ? UnitCondition.Normal : target.Condition;
        int days = condition == target.Condition ? target.ConditionDaysRemaining : 0;
        bool penalty = condition == target.Condition && target.HighSpiritsMoralePenaltyPending;
        world.Replace(target with { Soldiers = target.Soldiers + delivered, Morale = morale, Condition = condition,
            ConditionDaysRemaining = days, HighSpiritsMoralePenaltyPending = penalty });
        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, target.Id), ResourceKind.Soldiers,
            Kind, task.OriginSettlementId, delivered, delivered, events);
        if (success && condition != target.Condition)
            events.Add(new BattleConditionChangedEvent(world.Clock, target.Id, false, condition, 0)
            { SourceForceId = task.ForceId, Target = BattleObjectContext.Capture(target) });
        if (success)
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Leadership, "strategy-leadership-growth", events);
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new UnitStrategyResolvedEvent(world.Clock, actor.Id, target.Id, UnitStrategyKind.Reinforce,
            threshold, roll, success, occurrence) { RequestedSoldiers = requested, DeliveredSoldiers = delivered,
                Audience = StrategyAudience.Capture(world, task.ForceId, world.Units[target.Id]), PreviousMorale = target.Morale });
        var returning = task with { Reinforcements = delivered == troops.Soldiers && troops.Wounded == 0 ? null : troops with
        {
            Soldiers = troops.Soldiers - delivered,
            DemobilizationCityId = Demobilization.City(world, task.Journey!.DestinationPlaceId, rules.Personnel)
                ?? troops.DemobilizationCityId
        } };
        PersonnelReturns.Start(world, returning, rules.Personnel, events, true);
    }
}

internal sealed class UnitStrategyRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, Burn: null, Seize: null, Gossip: null,
          Route: { FieldDestination: true }, UnitStrategy: { } mission } &&
        Enum.IsDefined(mission.Strategy) && mission.TargetUnitId >= 0 &&
        (mission.Strategy == UnitStrategyKind.Reinforce
            ? task.Reinforcements is { Soldiers: > 0 and <= 3000, Morale: >= 0 and <= 100 } troops && (ulong)troops.Soldiers + troops.Wounded <= 3000
            : task.Reinforcements is null) &&
        DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => task.Journey is { Route: { } route } journey &&
        rules.Personnel.Places.ContainsKey(task.OriginSettlementId) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) &&
        (!route.FieldDestination || rules.Military?.RegionAt(route.Destination) == journey.DestinationPlaceId)
        ? Math.Max(0, route.Days - 1) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        UnitStrategies.Eligible(world, task.ForceId, task.Journey!.UnitStrategy!.Strategy, task.Journey.UnitStrategy.TargetUnitId, rules) &&
        (task.Journey.UnitStrategy.Strategy switch
        {
            UnitStrategyKind.FalseReport => world.Units[task.Journey.UnitStrategy.TargetUnitId].Condition is UnitCondition.Normal or UnitCondition.NoFormation,
            UnitStrategyKind.Disorder => world.Units[task.Journey.UnitStrategy.TargetUnitId].Condition == UnitCondition.Normal,
            UnitStrategyKind.Reinforce => world.Clock.DayNumber < task.Due.DayNumber || world.Units[task.Journey.UnitStrategy.TargetUnitId].Soldiers <
                rules.Military!.Capacity(world.Units[task.Journey.UnitStrategy.TargetUnitId]),
            _ => true
        });
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        UnitStrategies.Resolve(world, task, rules, events);
}
