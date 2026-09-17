using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public enum GiftKind { Money, Food, Treasure }
public sealed record GiftOffer(
    [property: JsonRequired] GiftKind Kind,
    [property: JsonRequired] uint Amount,
    [property: JsonRequired] int? TreasureId)
{
    [JsonIgnore] public bool IsValid => Kind switch
    {
        GiftKind.Money or GiftKind.Food => Amount is > 0 and <= 1000000 && TreasureId is null,
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
    private static ResourceAccount Account(int force) => new(ResourceOwnerKind.Force, force);

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

    private static CommandRejection Eligibility(WorldMutation world, int forceId, int officerId, int target,
        GiftOffer? offer, SimulationRules rules)
    {
        if (offer?.IsValid != true || !Target(world, forceId, target)) return CommandRejection.InvalidTask;
        if (!PersonnelMissions.CanAct(world, forceId, officerId)) return CommandRejection.PersonUnavailable;
        var origin = world.Settlements[world.Officers[officerId].SettlementId!.Value];
        if (ResourceAccounts.Resolve(world, forceId, origin.CorpsId).Kind != ResourceOwnerKind.Force)
            return CommandRejection.InvalidCorps;
        if (!rules.Personnel.Places.ContainsKey(origin.Id) || !Destination(world, target, rules, out _, out _))
            return CommandRejection.NoDestination;
        if (offer.Kind != GiftKind.Treasure)
            return ResourceAccounts.Balance(world, Account(forceId), Resource(offer)) >= offer.Amount
                ? CommandRejection.None : CommandRejection.InsufficientFunds;
        return world.Treasures.TryGetValue(offer.TreasureId!.Value, out var item) && item.Discovered &&
            item.HolderId is int holder && world.Officers[holder].OwnerId == forceId && world.Officers[holder].IsServing &&
            !world.Simulation.Tasks.Any(t => t.OfficerId == holder) && rules.Treasures.Items.ContainsKey(item.Id)
            ? CommandRejection.None : CommandRejection.TreasureUnavailable;
    }

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
            return new(CommandRejection.None, null, AdviserOpinion.Unavailable);
        return AdviserJudgment.Evaluate(world, forceId, ConditionalRandomPurpose.Gift, actor, target, Threshold(world, forceId, actor, target, offer, rules));
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, GiftCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.TargetForceId, command.Offer, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        Destination(world, command.TargetForceId, rules, out int destination, out var position);
        var route = PersonnelLocations.Route(world, rules.Personnel, origin, destination, position);
        int days = route.XunRoundedDays;
        var due = world.Clock;
        for (int i = 0; i < days; i++) due = due.NextDay();
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, origin,
            world.Clock, due, 0, command.Sequence)
        {
            Journey = new(destination, null, ConditionalRandom.Percentile(world.Simulation.InitialSeed,
                ConditionalRandomPurpose.Gift, world.Clock, actor.Id, command.TargetForceId)) { Route = route },
            Gift = new(command.TargetForceId, command.Offer)
        };
        if (command.Offer.Kind == GiftKind.Treasure)
        {
            var item = world.Treasures[command.Offer.TreasureId!.Value];
            if (item.HolderId != actor.Id) PersonnelTreasures.Transfer(world, item, actor.Id, command.IssuerId, "gift-envoy", events);
        }
        else ResourceAccounts.Change(world, Account(command.IssuerId), Resource(command.Offer), -(long)command.Offer.Amount,
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

    internal static void Cancel(WorldMutation world, PersistentTask task, List<DomainEvent> events) =>
        Refund(world, task, "gift-cancelled", events);

    private static uint Refund(WorldMutation world, PersistentTask task, string reason, List<DomainEvent> events)
    {
        if (task.Gift is not { } gift || gift.Offer.Kind == GiftKind.Treasure) return 0;
        var account = Account(task.ForceId);
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
                if (rules.Economy.ImperialSealId == item.Id) EconomicCycle.UpdateTrust(world, rules.Economy, events);
            }
            else
            {
                received = Math.Min(gift.Offer.Amount, ResourceAccounts.Receivable(world, Account(gift.TargetForceId), Resource(gift.Offer)));
                ResourceAccounts.Change(world, Account(gift.TargetForceId), Resource(gift.Offer), gift.Offer.Amount, "gift-accepted", task.ForceId, events);
            }
            ForceRelations.Set(world, task.ForceId, gift.TargetForceId,
                DiplomacyMath.GiftAcceptedRelation(MilitaryDiplomacy.Relation(world, task.ForceId, gift.TargetForceId)), events);
            var actor = world.Officers[task.OfficerId];
            if (actor.BaseAbilities.Politics < 100 && world.NextRandom("diplomacy-politics-growth") % 100 < 3)
            {
                world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Politics = actor.BaseAbilities.Politics + 1 } });
                events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "politics", actor.BaseAbilities.Politics, actor.BaseAbilities.Politics + 1));
            }
        }
        else
        {
            refunded = Refund(world, task, "gift-refused", events);
            ForceRelations.Change(world, task.ForceId, gift.TargetForceId, -5, events);
        }
        events.Add(new GiftResolvedEvent(world.Clock, task.Id, task.OfficerId, gift.TargetForceId, gift.Offer, accepted,
            received, refunded, gift.Offer.Amount - received - refunded));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class GiftRule(SimulationRules rules) : TaskRule
{
    internal override TaskResolutionPhase Phase => TaskResolutionPhase.XunBoundary;
    internal override bool HasValidPayload(PersistentTask task) => task.Gift is { TargetForceId: >= 0, Offer.IsValid: true } &&
        task.Gift.TargetForceId != task.ForceId && task.Reinforcements is null && task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
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
