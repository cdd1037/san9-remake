using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum UnitStrategyKind { FalseReport, Disorder, Inspire }
public sealed record UnitStrategyCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    UnitStrategyKind Strategy, int TargetUnitId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record UnitStrategyMission(UnitStrategyKind Strategy, int TargetUnitId);
public sealed record UnitStrategyResolvedEvent(GameClock Clock, int OfficerId, int TargetUnitId,
    UnitStrategyKind Strategy, int Threshold, int Roll, bool Success) : DomainEvent;

internal static class UnitStrategies
{
    internal const string Kind = "unit-strategy";

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int officerId, UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        var rejection = SubmissionEligibility(world, forceId, officerId, strategy, targetId, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        return AdviserJudgment.Evaluate(world, forceId, $"unit-strategy-{strategy}", officerId, targetId,
            Threshold(world, officerId, new(strategy, targetId), rules));
    }

    private static CommandRejection SubmissionEligibility(WorldMutation world, int forceId, int officerId,
        UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        if (!PersonnelMissions.CanAct(world, forceId, officerId)) return CommandRejection.PersonUnavailable;
        if (!Eligible(world, forceId, strategy, targetId, rules)) return CommandRejection.InvalidTask;
        int origin = world.Officers[officerId].SettlementId!.Value;
        return rules.Personnel.Places.ContainsKey(origin) && rules.Military!.RegionAt(world.Units[targetId].Position) is int region &&
            rules.Personnel.Places.ContainsKey(region) ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, UnitStrategyCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = SubmissionEligibility(world, command.IssuerId, command.OfficerId, command.Strategy, command.TargetUnitId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        var target = world.Units[command.TargetUnitId];
        var from = rules.Personnel.Places[origin];
        int region = rules.Military!.RegionAt(target.Position)!.Value;
        int days = PersonnelRulebook.JourneyDays(from.Position, target.Position);
        var due = world.Clock;
        for (int day = 0; day < Math.Max(0, days - 1); day++) due = due.NextDay();
        var purpose = command.Strategy switch
        {
            UnitStrategyKind.FalseReport => ConditionalRandomPurpose.FalseReport,
            UnitStrategyKind.Disorder => ConditionalRandomPurpose.Disorder,
            UnitStrategyKind.Inspire => ConditionalRandomPurpose.Inspire,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, purpose, world.Clock, actor.Id, target.Id);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, origin,
            world.Clock, due, 0, command.Sequence)
        {
            Journey = new(region, null, roll) { DestinationPosition = target.Position, UnitStrategy = new(command.Strategy, target.Id) }
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static OfficerState[] Members(WorldMutation world, UnitState target) => world.Officers.Values
        .Where(p => p.IsServing && p.UnitId == target.Id && p.OwnerId == target.OwnerId).OrderBy(p => p.Id).ToArray();

    private static bool Knows(WorldMutation world, int force, IEnumerable<OfficerState> members, TacticEffect effect, SimulationRules rules) =>
        rules.Military!.Tactics.Values.Where(t => t.Effect == effect).Any(t =>
            MilitaryTactics.HasTactic(world, force, members.Select(p => p.Id).ToArray(), t.Id, rules.Treasures));

    internal static bool Eligible(WorldMutation world, int force, UnitStrategyKind strategy, int targetId, SimulationRules rules)
    {
        if (!Enum.IsDefined(strategy) || rules.Military is not { } military || !world.Units.TryGetValue(targetId, out var target) ||
            target.IsPersonnel || target.IsEmbedded || target.FormationId is not int formationId ||
            !military.Formations.TryGetValue(formationId, out var formation) || Members(world, target).Length == 0) return false;
        if (strategy == UnitStrategyKind.Inspire)
            return target.OwnerId == force && target.Condition != UnitCondition.HighSpirits &&
                (target.Condition != UnitCondition.Normal || target.Morale < 100);
        if (!MilitaryCombat.Hostile(world, force, target.OwnerId) ||
            !world.Relations.ContainsKey((Math.Min(force, target.OwnerId), Math.Max(force, target.OwnerId)))) return false;
        return strategy != UnitStrategyKind.Disorder || !formation.ImmuneToDisorder;
    }

    internal static int Threshold(WorldMutation world, int actorId, UnitStrategyMission mission, SimulationRules rules)
    {
        var target = world.Units[mission.TargetUnitId];
        var members = Members(world, target);
        int intelligence = world.Abilities(world.Officers[actorId]).Intelligence;
        if (mission.Strategy == UnitStrategyKind.Inspire)
            return StrategyMath.InspireThreshold(intelligence, Knows(world, world.Officers[actorId].OwnerId!.Value,
                [world.Officers[actorId]], TacticEffect.Inspire, rules));
        if (Knows(world, target.OwnerId, members, TacticEffect.CounterConfusion, rules) ||
            (mission.Strategy == UnitStrategyKind.FalseReport ? target.Condition is not (UnitCondition.Normal or UnitCondition.NoFormation)
                : target.Condition != UnitCondition.Normal)) return 0;
        bool defensive = rules.Military!.Formations[target.FormationId!.Value].HalvesHostileStrategyChance;
        return mission.Strategy == UnitStrategyKind.FalseReport
            ? StrategyMath.FalseReportThreshold(intelligence, members.Max(p => world.Abilities(p).Intelligence), defensive)
            : StrategyMath.DisorderThreshold(intelligence, members.Max(p => world.Abilities(p).Leadership), defensive);
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, List<DomainEvent> events)
    {
        var mission = task.Journey!.UnitStrategy!;
        var target = world.Units[mission.TargetUnitId];
        int threshold = Threshold(world, task.OfficerId, mission, rules);
        int roll = task.Journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        var members = Members(world, target);
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
                AwaitingPostAction = condition == UnitCondition.Disguised || target.AwaitingPostAction,
                EngagementTargetId = condition == UnitCondition.Disguised ? null : target.EngagementTargetId });
            if (condition == UnitCondition.Disguised)
            {
                events.Add(new UnitOrderChangedEvent(world.Clock, target.Id, null, "false-report"));
                if (target.EngagementTargetId is not null) events.Add(new UnitEngagementChangedEvent(world.Clock, target.Id, null, "false-report"));
            }
            events.Add(new BattleConditionChangedEvent(world.Clock, target.Id, false, condition, days));
            BattleLearning.Passive(world, members, TacticEffect.CounterConfusion, rules.Military!, events);
            var actor = world.Officers[task.OfficerId];
            if (actor.BaseAbilities.Intelligence < 100 && world.NextRandom("strategy-intelligence-growth") % 100 < 3)
            {
                int next = actor.BaseAbilities.Intelligence + 1;
                world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Intelligence = next } });
                events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "intelligence", actor.BaseAbilities.Intelligence, next));
            }
        }
        else if (mission.Strategy == UnitStrategyKind.Inspire)
            world.Replace(target with { Morale = Math.Max(1, target.Morale - 1 - (int)(world.NextRandom("strategy-inspire-failure-morale") % 5)) });
        else ForceRelations.Change(world, task.ForceId, target.OwnerId,
            -1 - (int)(world.NextRandom("strategy-hostile-failure-relations") % 3), events);
        OfficerTacticGrowth.Increase(world, task.OfficerId, 7, 2, events);
        OfficerTacticGrowth.Increase(world, task.OfficerId, 8, 2, events);
        events.Add(new UnitStrategyResolvedEvent(world.Clock, task.OfficerId, target.Id, mission.Strategy, threshold, roll, success));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class UnitStrategyRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
          DestinationPosition: { X: >= 0, Y: >= 0 }, UnitStrategy: { } mission } &&
        Enum.IsDefined(mission.Strategy) && mission.TargetUnitId >= 0 && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => rules.Military is not null && task.Journey is
        { DestinationPosition: { } position } journey && rules.Personnel.Places.TryGetValue(task.OriginSettlementId, out var from) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) && rules.Military.RegionAt(position) == journey.DestinationPlaceId
        ? Math.Max(0, PersonnelRulebook.JourneyDays(from.Position, position) - 1) : -1;
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        UnitStrategies.Eligible(world, task.ForceId, task.Journey!.UnitStrategy!.Strategy, task.Journey.UnitStrategy.TargetUnitId, rules) &&
        (task.Journey.UnitStrategy.Strategy switch
        {
            UnitStrategyKind.FalseReport => world.Units[task.Journey.UnitStrategy.TargetUnitId].Condition is UnitCondition.Normal or UnitCondition.NoFormation,
            UnitStrategyKind.Disorder => world.Units[task.Journey.UnitStrategy.TargetUnitId].Condition == UnitCondition.Normal,
            _ => true
        });
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        UnitStrategies.Resolve(world, task, rules, events);
}
