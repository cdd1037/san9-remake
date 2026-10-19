using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SeizeCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetSettlementId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record SeizeMission(int TargetSettlementId);
public enum SeizeOutcomeKind { Item, Person, Money, Failure }
public sealed record SeizeResolvedEvent(GameClock Clock, int OfficerId, int TargetSettlementId, int Roll,
    int ItemThreshold, int PersonThreshold, int MoneyThreshold, bool ItemGatePassed, SeizeOutcomeKind Outcome) : DomainEvent
{
    public int? TreasureId { get; init; }
    public int? PreviousHolderId { get; init; }
    public int? RecoveredOfficerId { get; init; }
    public uint Money { get; init; }
    public int InjuryBefore { get; init; }
    public int InjuryAfter { get; init; }
}

internal static class SeizeStrategies
{
    internal const string Kind = "strategy-seize";
    private sealed record Thresholds(int Item, int Person, int Money,
        TreasureState? ItemCandidate, OfficerState[] PersonCandidates);

    private static bool Eligible(WorldMutation world, int forceId, int origin, int targetId, SimulationRules rules) =>
        BuildingStrategyTargets.Eligible(world, forceId, origin, targetId, rules) &&
        ResourceAccounts.Root(world, world.Settlements[targetId].OwnerId!.Value).Treasury.Money > 0;

    internal static bool EligibleForTask(WorldMutation world, PersistentTask task, SimulationRules rules) =>
        Eligible(world, task.ForceId, task.OriginSettlementId, task.Journey!.Seize!.TargetSettlementId, rules);

    internal static CommandRejection Eligibility(WorldMutation world, int forceId, int officerId, int targetId,
        SimulationRules rules)
    {
        if (!PersonnelMissions.CanStartMission(world, forceId, officerId, rules.Military)) return CommandRejection.PersonUnavailable;
        return Eligible(world, forceId, world.Officers[officerId].SettlementId!.Value, targetId, rules)
            ? CommandRejection.None : CommandRejection.InvalidTask;
    }

    private static TreasureState? ItemCandidate(WorldMutation world, int targetId, TreasureRulebook rules) =>
        world.Treasures.Values.Where(item => item.Discovered && item.HolderId is int holder &&
                rules.Items.ContainsKey(item.Id) && world.Officers.TryGetValue(holder, out var person) &&
                person.SettlementId == targetId)
            .OrderBy(item => rules.Items[item.Id].LoyaltyBonus).ThenBy(item => item.Id).FirstOrDefault();

    private static OfficerState[] PersonCandidates(WorldMutation world, int forceId, int targetId) =>
        world.Officers.Values.Where(person => person.Status == OfficerStatus.Captive &&
            person.OwnerId == forceId && person.SettlementId == targetId).OrderBy(person => person.Id).ToArray();

    private static Thresholds ComputeThresholds(WorldMutation world, int officerId, int targetId,
        SimulationRules rules)
    {
        var actor = world.Officers[officerId];
        int war = world.Abilities(actor).War;
        var item = ItemCandidate(world, targetId, rules.Treasures);
        int itemThreshold = 0;
        if (item?.HolderId is int holder)
        {
            var abilities = world.Abilities(world.Officers[holder]);
            int maximum = Math.Max(Math.Max(abilities.War, abilities.Intelligence),
                Math.Max(abilities.Politics, abilities.Leadership));
            itemThreshold = StrategyMath.SeizeItemThreshold(war, rules.Treasures.Items[item.Id].LoyaltyBonus, maximum);
        }
        var people = PersonCandidates(world, actor.OwnerId!.Value, targetId);
        return new(itemThreshold, StrategyMath.SeizePersonThreshold(war, people.Length),
            DecisionThreshold(world, officerId, targetId), item, people);
    }

    internal static int DecisionThreshold(WorldMutation world, int officerId, int targetId) => StrategyMath.SeizeMoneyThreshold(
        world.Abilities(world.Officers[officerId]).War,
        LeadershipMaintenance.Commander(world, targetId) is { } commander ? world.Abilities(commander).Leadership : 0,
        world.Settlements[targetId].Soldiers);

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int officerId, int targetId,
        SimulationRules rules)
    {
        var rejection = Eligibility(world, forceId, officerId, targetId, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        var thresholds = ComputeThresholds(world, officerId, targetId, rules);
        int withoutItem = Math.Clamp(Math.Max(thresholds.Person, thresholds.Money), 0, 100);
        int withItem = Math.Clamp(Math.Max(thresholds.Item, withoutItem), 0, 100);
        return AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Seize, officerId, targetId,
            (withoutItem + withItem) / 2.0);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, SeizeCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var plan = MissionPreparation.Prepare(world, command, rules, out var rejection);
        if (plan is null) return rejection;
        var actor = world.Officers[command.OfficerId];
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Seize,
            world.Clock, actor.Id, command.TargetSettlementId);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id,
            plan.Origin, world.Clock, plan.Arrival, 0, command.Sequence)
        {
            Journey = new(command.TargetSettlementId, null, roll)
            {
                Route = plan.Route,
                Seize = new(command.TargetSettlementId)
            }
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules,
        List<DomainEvent> events)
    {
        int targetId = task.Journey!.Seize!.TargetSettlementId;
        var target = world.Settlements[targetId];
        var actor = world.Officers[task.OfficerId];
        var thresholds = ComputeThresholds(world, actor.Id, targetId, rules);
        bool itemGate = world.NextRandom("strategy-seize-item-gate") % 100 < 50;
        if (!itemGate) thresholds = thresholds with { Item = 0 };
        int roll = task.Journey.SuccessRoll!.Value;
        int injuryBefore = actor.Injury;
        int? treasureId = null;
        int? previousHolderId = null;
        int? recoveredId = null;
        uint money = 0;
        SeizeOutcomeKind outcome;
        if (roll < thresholds.Item)
        {
            var item = thresholds.ItemCandidate!;
            treasureId = item.Id;
            previousHolderId = item.HolderId;
            PersonnelTreasures.Transfer(world, item, actor.Id, task.ForceId, Kind, events);
            outcome = SeizeOutcomeKind.Item;
        }
        else if (roll < thresholds.Person)
        {
            var person = thresholds.PersonCandidates[world.NextRandom("strategy-seize-person") %
                (ulong)thresholds.PersonCandidates.Length];
            recoveredId = person.Id;
            if (PrisonerRelease.Recover(world, person.Id, targetId, task.Sequence,
                rules.Personnel, rules.Economy, events) != CommandRejection.None)
                throw new InvalidOperationException("Validated seize prisoner recovery failed.");
            outcome = SeizeOutcomeKind.Person;
        }
        else if (roll < thresholds.Money)
        {
            uint requested = StrategyMath.SeizeMoney(target.Soldiers, target.Wounded,
                ResourceAccounts.Root(world, target.OwnerId!.Value).Treasury.Money,
                (int)(world.NextRandom("strategy-seize-money") % 50));
            var source = ResourceAccounts.Resolve(world, target.OwnerId.Value, null);
            var recipient = ResourceAccounts.Resolve(world, task.ForceId,
                world.Settlements[task.OriginSettlementId].CorpsId);
            money = Math.Min(requested, ResourceAccounts.Receivable(world, recipient, ResourceKind.Money));
            ResourceAccounts.Change(world, source, ResourceKind.Money, -(long)money, Kind, targetId, events, true);
            ResourceAccounts.Change(world, recipient, ResourceKind.Money, money, Kind, targetId, events);
            outcome = SeizeOutcomeKind.Money;
        }
        else
        {
            int injury = Math.Max(actor.Injury,
                (int)Math.Min(7, actor.Injury + ((long)roll - thresholds.Money) / 10));
            world.Replace(actor with { Injury = injury });
            ForceRelations.Change(world, task.ForceId, target.OwnerId!.Value,
                -1 - (int)(world.NextRandom("strategy-seize-failure-relations") % 3), events);
            outcome = SeizeOutcomeKind.Failure;
        }
        if (outcome != SeizeOutcomeKind.Failure)
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.War, "strategy-war-growth", events);
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new SeizeResolvedEvent(world.Clock, actor.Id, targetId, roll, thresholds.Item,
            thresholds.Person, thresholds.Money, itemGate, outcome)
        {
            TreasureId = treasureId,
            PreviousHolderId = previousHolderId,
            RecoveredOfficerId = recoveredId,
            Money = money,
            InjuryBefore = injuryBefore,
            InjuryAfter = world.Officers[actor.Id].Injury
        });
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class SeizeRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
    {
        TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null,
          UnitStrategy: null, Burn: null, Seize: { TargetSettlementId: >= 0 } seize, Gossip: null,
        Route: { FieldDestination: false }
    } journey &&
        journey.DestinationPlaceId == seize.TargetSettlementId &&
        rules.Personnel.Places.TryGetValue(seize.TargetSettlementId, out var target) &&
        DurationDays(task) >= 0;

    internal override int DurationDays(PersistentTask task) => task.Journey is { Route: { } route } journey &&
        rules.Personnel.Places.ContainsKey(task.OriginSettlementId) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) &&
        (!route.FieldDestination || rules.Military?.RegionAt(route.Destination) == journey.DestinationPlaceId)
        ? Math.Max(0, route.Days - 1) : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        SeizeStrategies.EligibleForTask(world, task, rules);

    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        SeizeStrategies.Resolve(world, task, rules, events);
}
