using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record GossipCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    int FirstTargetForceId, int SecondTargetForceId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record GossipMission(int FirstTargetForceId, int SecondTargetForceId);
public sealed record GossipResolvedEvent(GameClock Clock, int OfficerId, int FirstTargetForceId,
    int SecondTargetForceId, int Threshold, int Roll, bool Success, int RelationLoss,
    int TargetOfficerId, bool LoyaltyGatePassed, int LoyaltyLoss) : DomainEvent;

internal static class GossipStrategies
{
    internal const string Kind = "strategy-gossip";

    private static bool PlayerControlled(WorldMutation world, int forceId) =>
        world.Simulation.Controls.Any(control => control.ForceId == forceId && control.Kind == ControllerKind.Player);

    private static OfficerState[] Targets(WorldMutation world, int forceId) => world.Officers.Values
        .Where(person => person.OwnerId == forceId && person.IsVisible && person.Status != OfficerStatus.Ruler)
        .OrderBy(person => person.Loyalty).ThenBy(person => person.Id).ToArray();

    private static int MemberCount(WorldMutation world, int forceId) => world.Officers.Values.Count(person =>
        person.OwnerId == forceId && person.IsVisible);

    private static bool ValidForce(WorldMutation world, int forceId) =>
        world.Forces.TryGetValue(forceId, out var force) && !force.IsEliminated && !force.IsBarbarian &&
        force.LeaderId is int leader && world.Officers.TryGetValue(leader, out var person) && person.IsVisible;

    private static bool EligibleTargets(WorldMutation world, int actorForceId, int firstTargetForceId,
        int secondTargetForceId)
    {
        if (!ValidForce(world, firstTargetForceId) || !ValidForce(world, secondTargetForceId) ||
            actorForceId == firstTargetForceId || actorForceId == secondTargetForceId ||
            firstTargetForceId == secondTargetForceId ||
            world.Simulation.Alliance?.Protects(actorForceId, firstTargetForceId) == true ||
            MemberCount(world, firstTargetForceId) <= 1 || Targets(world, firstTargetForceId).Length == 0) return false;
        return world.Relations.ContainsKey((Math.Min(actorForceId, firstTargetForceId),
                   Math.Max(actorForceId, firstTargetForceId))) &&
               world.Relations.ContainsKey((Math.Min(firstTargetForceId, secondTargetForceId),
                   Math.Max(firstTargetForceId, secondTargetForceId)));
    }

    private static bool TryDestination(WorldMutation world, int firstTargetForceId, SimulationRules rules,
        out int placeId, out WorldPosition? position)
    {
        position = null;
        var leader = world.Officers[world.Forces[firstTargetForceId].LeaderId!.Value];
        if (leader.SettlementId is int settlement && world.Settlements.TryGetValue(settlement, out var place) &&
            place.CanHostPersons && place.Durability > 0 && rules.Personnel.Places.ContainsKey(settlement))
        {
            placeId = settlement;
            return true;
        }
        if (leader.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit) &&
            rules.Military?.RegionAt(unit.Position) is int region && rules.Personnel.Places.ContainsKey(region))
        {
            placeId = region;
            position = unit.Position;
            return true;
        }
        placeId = -1;
        return false;
    }

    private static CommandRejection Eligibility(WorldMutation world, int forceId, int officerId,
        int firstTargetForceId, int secondTargetForceId, SimulationRules rules)
    {
        if (!PersonnelMissions.CanAct(world, forceId, officerId)) return CommandRejection.PersonUnavailable;
        if (!EligibleTargets(world, forceId, firstTargetForceId, secondTargetForceId) ||
            !rules.Personnel.Places.ContainsKey(world.Officers[officerId].SettlementId!.Value) ||
            !TryDestination(world, firstTargetForceId, rules, out _, out _)) return CommandRejection.InvalidTask;
        return CommandRejection.None;
    }

    internal static int Threshold(WorldMutation world, int officerId, int firstTargetForceId,
        int secondTargetForceId)
    {
        var target = world.Forces[firstTargetForceId];
        int leaderIntelligence = world.Abilities(world.Officers[target.LeaderId!.Value]).Intelligence;
        int? adviserIntelligence = target.AdviserId is int adviser
            ? world.Abilities(world.Officers[adviser]).Intelligence : null;
        return StrategyMath.GossipThreshold(world.Abilities(world.Officers[officerId]).Intelligence,
            leaderIntelligence, adviserIntelligence,
            PlayerControlled(world, firstTargetForceId) && PlayerControlled(world, secondTargetForceId));
    }

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int officerId,
        int firstTargetForceId, int secondTargetForceId, SimulationRules rules)
    {
        var rejection = Eligibility(world, forceId, officerId, firstTargetForceId, secondTargetForceId, rules);
        return rejection == CommandRejection.None
            ? AdviserJudgment.Evaluate(world, forceId, Kind, officerId, firstTargetForceId,
                secondTargetForceId, Threshold(world, officerId, firstTargetForceId, secondTargetForceId))
            : new(rejection, null, AdviserOpinion.Unavailable);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, GossipCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.FirstTargetForceId,
            command.SecondTargetForceId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        _ = TryDestination(world, command.FirstTargetForceId, rules, out int destination,
            out WorldPosition? position);
        int offset = position is { } field
            ? Math.Max(0, PersonnelRulebook.JourneyDays(rules.Personnel.Places[origin].Position, field) - 1)
            : rules.Personnel.ResolutionDayOffset(origin, destination, PersonnelJourneyKind.Move);
        var due = world.Clock;
        for (int day = 0; day < offset; day++) due = due.NextDay();
        int roll = ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.Gossip,
            world.Clock, actor.Id, command.FirstTargetForceId, command.SecondTargetForceId);
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id,
            origin, world.Clock, due, 0, command.Sequence)
        {
            Journey = new(destination, null, roll)
            {
                DestinationPosition = position,
                Gossip = new(command.FirstTargetForceId, command.SecondTargetForceId)
            }
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static bool EligibleForTask(WorldMutation world, PersistentTask task) => EligibleTargets(world,
        task.ForceId, task.Journey!.Gossip!.FirstTargetForceId, task.Journey.Gossip.SecondTargetForceId);

    internal static void Resolve(WorldMutation world, PersistentTask task, PersonnelRulebook places,
        List<DomainEvent> events)
    {
        var mission = task.Journey!.Gossip!;
        var actor = world.Officers[task.OfficerId];
        var target = Targets(world, mission.FirstTargetForceId)[0];
        int threshold = Threshold(world, actor.Id, mission.FirstTargetForceId, mission.SecondTargetForceId);
        int roll = task.Journey.SuccessRoll!.Value;
        bool success = roll < threshold;
        int relationLoss;
        bool loyaltyGate = false;
        int loyaltyLoss = 0;
        if (success)
        {
            int current = world.Relations[(Math.Min(mission.FirstTargetForceId, mission.SecondTargetForceId),
                Math.Max(mission.FirstTargetForceId, mission.SecondTargetForceId))].Value;
            relationLoss = StrategyMath.GossipRelationLoss(current);
            ForceRelations.Change(world, mission.FirstTargetForceId, mission.SecondTargetForceId,
                -relationLoss, events);
            int loyaltyChance = Math.Clamp(100 - world.Abilities(target).Intelligence, 0, 100);
            loyaltyGate = world.NextRandom("strategy-gossip-loyalty-gate") % 100 < (ulong)loyaltyChance;
            if (loyaltyGate)
            {
                loyaltyLoss = 3 + (int)(world.NextRandom("strategy-gossip-loyalty-loss") % 2);
                RecruitmentRules.SetLoyalty(world, target.Id, mission.FirstTargetForceId,
                    Math.Max(0, target.Loyalty - loyaltyLoss), Kind, events);
            }
            if (actor.BaseAbilities.Intelligence < 100 &&
                world.NextRandom("strategy-intelligence-growth") % 100 < 3)
            {
                int next = actor.BaseAbilities.Intelligence + 1;
                world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Intelligence = next } });
                events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "intelligence",
                    actor.BaseAbilities.Intelligence, next));
            }
        }
        else
        {
            relationLoss = 1 + (int)(world.NextRandom("strategy-gossip-failure-relations") % 3);
            ForceRelations.Change(world, task.ForceId, mission.FirstTargetForceId, -relationLoss, events);
        }
        OfficerTacticGrowth.Increase(world, actor.Id, 7, 2, events);
        OfficerTacticGrowth.Increase(world, actor.Id, 8, 2, events);
        events.Add(new GossipResolvedEvent(world.Clock, actor.Id, mission.FirstTargetForceId,
            mission.SecondTargetForceId, threshold, roll, success, relationLoss, target.Id,
            loyaltyGate, loyaltyLoss));
        PersonnelReturns.Start(world, task, places, events, true);
    }
}

internal sealed class GossipRule(SimulationRules rules) : TaskRule
{
    internal override bool HasValidPayload(PersistentTask task) => task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
          UnitStrategy: null, Burn: null, Seize: null,
          Gossip: { FirstTargetForceId: >= 0, SecondTargetForceId: >= 0 } gossip, DestinationPosition: var position } &&
        gossip.FirstTargetForceId != gossip.SecondTargetForceId &&
        (position is not null || rules.Personnel.Places.ContainsKey(task.Journey.DestinationPlaceId)) &&
        DurationDays(task) >= 0;

    internal override int DurationDays(PersistentTask task) => task.Journey is { } journey &&
        rules.Personnel.Places.TryGetValue(task.OriginSettlementId, out var origin) &&
        rules.Personnel.Places.ContainsKey(journey.DestinationPlaceId) &&
        (journey.DestinationPosition is not { } position || rules.Military?.RegionAt(position) == journey.DestinationPlaceId)
        ? journey.DestinationPosition is { } field
            ? Math.Max(0, PersonnelRulebook.JourneyDays(origin.Position, field) - 1)
            : rules.Personnel.ResolutionDayOffset(task.OriginSettlementId, journey.DestinationPlaceId,
                PersonnelJourneyKind.Move)
        : -1;

    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        GossipStrategies.EligibleForTask(world, task);

    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        GossipStrategies.Resolve(world, task, rules.Personnel, events);
}
