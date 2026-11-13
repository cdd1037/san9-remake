using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum GiftKind { Money, Food, Treasure }
public sealed record GiftOffer(
    [property: JsonRequired] GiftKind Kind,
    [property: JsonRequired] uint Amount,
    [property: JsonRequired] int? TreasureId)
{
    public const uint MinimumMoney = 1000;
    public const uint MinimumFood = 10000;
    public const uint MaximumMoney = 10000;
    public const uint MaximumFood = 100000;

    public static uint MinimumAmount(GiftKind kind) => kind switch
    {
        GiftKind.Money => MinimumMoney,
        GiftKind.Food => MinimumFood,
        _ => throw new ArgumentException("A gift amount must select money or food.", nameof(kind))
    };

    public static uint MaximumAmount(GiftKind kind) => kind switch
    {
        GiftKind.Money => MaximumMoney,
        GiftKind.Food => MaximumFood,
        _ => throw new ArgumentException("A gift amount must select money or food.", nameof(kind))
    };

    [JsonIgnore] public bool IsValid => Kind switch
    {
        GiftKind.Money or GiftKind.Food => Amount >= MinimumAmount(Kind) && Amount <= MaximumAmount(Kind) && TreasureId is null,
        GiftKind.Treasure => Amount == 0 && TreasureId is >= 0,
        _ => false
    };
}
public sealed record GiftCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId, int TargetForceId,
    GiftOffer Offer) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record GiftMission(
    [property: JsonRequired] int TargetForceId,
    [property: JsonRequired] GiftOffer Offer);
public sealed record GiftResolvedEvent(GameClock Clock, Guid TaskId, int OfficerId, int TargetForceId,
    GiftOffer Offer, bool Accepted, uint Received, uint Refunded, uint Spent) : DomainEvent;

internal static class GiftDiplomacy
{
    internal const string Kind = "diplomacy-gift";
    private static ResourceKind Resource(GiftOffer offer) => offer.Kind == GiftKind.Money ? ResourceKind.Money : ResourceKind.Food;
    private static ResourceAccount Account(WorldMutation world, int force) => ResourceAccounts.Resolve(world, force, null);

    internal static bool Target(WorldMutation world, int force, int target) => force != target &&
        world.Forces.TryGetValue(target, out var other) && !other.IsEliminated && other.LeaderId is int leader &&
        world.Officers.TryGetValue(leader, out var ruler) && ruler.IsServing && ruler.OwnerId == target;

    internal static bool Destination(WorldMutation world, int target, SimulationRules rules, out int placeId, out WorldPosition? position)
    {
        position = null;
        var ruler = world.Officers[world.Forces[target].LeaderId!.Value];
        if (ruler.SettlementId is int home && world.Settlements.TryGetValue(home, out var place) &&
            place.CanHostPersons && place.Durability > 0 && rules.Personnel.Places.ContainsKey(home))
        { placeId = home; return true; }
        if (ruler.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit) &&
            rules.Military?.RegionAt(unit.Position) is int region && rules.Personnel.Places.ContainsKey(region))
        { placeId = region; position = unit.Position; return true; }
        placeId = -1;
        return false;
    }

    internal static CommandRejection Eligibility(WorldMutation world, int forceId, int officerId, int target,
        GiftOffer? offer, SimulationRules rules)
    {
        if (offer?.IsValid != true || !Target(world, forceId, target)) return CommandRejection.InvalidTask;
        if (!PersonnelMissions.CanStartMission(world, forceId, officerId, rules.Military)) return CommandRejection.PersonUnavailable;
        var origin = world.Settlements[world.Officers[officerId].SettlementId!.Value];
        if (ResourceAccounts.Resolve(world, forceId, origin.CorpsId).Id != ResourceAccounts.Root(world, forceId).Id)
            return CommandRejection.InvalidCorps;
        if (!rules.Personnel.Places.ContainsKey(origin.Id) || !Destination(world, target, rules, out _, out _))
            return CommandRejection.NoDestination;
        if (offer.Kind != GiftKind.Treasure)
            return ResourceAccounts.Balance(world, Account(world, forceId), Resource(offer)) >= offer.Amount
                ? CommandRejection.None : CommandRejection.InsufficientFunds;
        return CanOfferTreasure(world, forceId, offer.TreasureId!.Value, rules.Treasures)
            ? CommandRejection.None : CommandRejection.TreasureUnavailable;
    }

    internal static bool CanOfferTreasure(WorldMutation world, int forceId, int treasureId, TreasureRulebook rules) =>
        world.Treasures.TryGetValue(treasureId, out var item) && item.Discovered &&
            item.HolderId is int holder && world.Forces.TryGetValue(forceId, out var force) && holder == force.LeaderId &&
            world.Officers[holder].OwnerId == forceId && world.Officers[holder].IsServing &&
            !world.IsBusy(holder) && rules.Items.ContainsKey(item.Id);

    internal static int Threshold(WorldMutation world, int forceId, int actor, int target, GiftOffer offer, SimulationRules rules)
    {
        int politics = world.Abilities(world.Officers[actor]).Politics;
        int relation = MilitaryDiplomacy.Relation(world, forceId, target);
        return offer.Kind switch
        {
            GiftKind.Money => DiplomacyMath.GiftMoneyThreshold(politics, relation, offer.Amount),
            GiftKind.Food => DiplomacyMath.GiftFoodThreshold(politics, relation, offer.Amount),
            _ => DiplomacyMath.GiftTreasureThreshold(politics, relation, rules.Treasures.Items[offer.TreasureId!.Value].LoyaltyBonus)
        };
    }

    internal static CommandAdvice Advice(WorldMutation world, int forceId, int actor, int target, GiftOffer offer, SimulationRules rules)
    {
        var rejection = Eligibility(world, forceId, actor, target, offer, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        if (world.Simulation.Controls.Any(c => c.ForceId == target && c.Kind == ControllerKind.Player))
            return AdviserJudgment.Unpredictable(world, forceId);
        return AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Gift, actor, target, Threshold(world, forceId, actor, target, offer, rules));
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, GiftCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        var plan = MissionPreparation.Prepare(world, command, rules, out var rejection);
        if (plan is null) return rejection;
        var actor = world.Officers[command.OfficerId];
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, plan.Origin,
            world.Clock, plan.Arrival, 0, command.Sequence)
        {
            Journey = new(plan.Destination, null, ConditionalRandom.Percentile(world.Simulation.InitialSeed,
                ConditionalRandomPurpose.Gift, world.Clock, actor.Id, command.TargetForceId)) { Route = plan.Route },
            Gift = new(command.TargetForceId, command.Offer)
        };
        if (command.Offer.Kind == GiftKind.Treasure)
        {
            var item = world.Treasures[command.Offer.TreasureId!.Value];
            if (item.HolderId != actor.Id) PersonnelTreasures.Transfer(world, item, actor.Id, command.IssuerId, "gift-envoy", events);
        }
        else ResourceAccounts.Change(world, Account(world, command.IssuerId), Resource(command.Offer), -(long)command.Offer.Amount,
            "gift-departure", actor.Id, events, true);
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static bool Valid(WorldMutation world, PersistentTask task, SimulationRules rules) => task.Gift is { } gift &&
        Target(world, task.ForceId, gift.TargetForceId) && (gift.Offer.Kind != GiftKind.Treasure ||
        world.Treasures.TryGetValue(gift.Offer.TreasureId!.Value, out var item) && item.Discovered && item.HolderId == task.OfficerId &&
        rules.Treasures.Items.ContainsKey(item.Id));

    private static uint Refund(WorldMutation world, PersistentTask task, string reason, List<DomainEvent> events)
    {
        if (task.Gift is not { } gift || gift.Offer.Kind == GiftKind.Treasure) return 0;
        var account = Account(world, task.ForceId);
        var resource = Resource(gift.Offer);
        uint refunded = Math.Min(gift.Offer.Amount, ResourceAccounts.Receivable(world, account, resource));
        ResourceAccounts.Change(world, account, resource, gift.Offer.Amount, reason, task.OfficerId, events);
        return refunded;
    }

    internal static void Resolve(WorldMutation world, PersistentTask task, SimulationRules rules, bool accepted, List<DomainEvent> events)
    {
        var gift = task.Gift!;
        uint received = 0;
        uint refunded = 0;
        if (accepted)
        {
            if (gift.Offer.Kind == GiftKind.Treasure)
            {
                var item = world.Treasures[gift.Offer.TreasureId!.Value];
                PersonnelTreasures.Transfer(world, item, world.Forces[gift.TargetForceId].LeaderId!.Value, task.ForceId, "gift-accepted", events);
            }
            else
            {
                received = Math.Min(gift.Offer.Amount, ResourceAccounts.Receivable(world, Account(world, gift.TargetForceId), Resource(gift.Offer)));
                ResourceAccounts.Change(world, Account(world, gift.TargetForceId), Resource(gift.Offer), gift.Offer.Amount, "gift-accepted", task.ForceId, events);
            }
            int relation = MilitaryDiplomacy.Relation(world, task.ForceId, gift.TargetForceId);
            ForceRelations.Change(world, task.ForceId, gift.TargetForceId, Math.Max(60 - relation, 20), rules.Military, events);
            var actor = world.Officers[task.OfficerId];
            OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Politics, "diplomacy-politics-growth", events);
            OfficerServiceMerit.Credit(world, actor.Id, task.ForceId, 1, "gift-accepted", events);
        }
        else
        {
            refunded = Refund(world, task, "gift-refused", events);
            ForceRelations.Change(world, task.ForceId, gift.TargetForceId, -5, rules.Military, events);
        }
        events.Add(new GiftResolvedEvent(world.Clock, task.Id, task.OfficerId, gift.TargetForceId, gift.Offer, accepted,
            received, refunded, gift.Offer.Amount - received - refunded));
        PersonnelReturns.Start(world, task, rules.Personnel, events, world.CurrentDateCountdownConsumed);
    }
}

internal sealed class GiftRule(SimulationRules rules) : TaskRule
{
    internal override TaskResolutionPhase Phase => TaskResolutionPhase.XunBoundary;
    internal override bool HasValidPayload(PersistentTask task) => task.Gift is { TargetForceId: >= 0, Offer.IsValid: true } &&
        task.Gift.TargetForceId != task.ForceId && task.Reinforcements is null && task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null } && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => DiplomacyJourney.Duration(task, rules);
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) && GiftDiplomacy.Valid(world, task, rules);
    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task) => world.Simulation.Controls.Any(c =>
        c.ForceId == task.Gift!.TargetForceId && c.Kind == ControllerKind.Player) ? task.Gift!.TargetForceId : null;
    internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        GiftDiplomacy.Resolve(world, task, rules, task.Journey!.SuccessRoll < GiftDiplomacy.Threshold(world, task.ForceId,
            task.OfficerId, task.Gift!.TargetForceId, task.Gift.Offer, rules), events);
    internal override void CompleteDecision(WorldMutation world, PersistentTask task, bool accept, List<DomainEvent> events) =>
        GiftDiplomacy.Resolve(world, task, rules, accept, events);
}
