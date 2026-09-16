using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record RequestAttackCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    int TargetForceId, int TargetSettlementId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record AttackRequestMission([property: JsonRequired] int TargetForceId,
    [property: JsonRequired] int TargetSettlementId, [property: JsonRequired] CompensationOffer? Offer);
public sealed record AttackRequestPrompt(Guid TaskId, int Step, int RespondingForceId, int RequesterForceId,
    int TargetForceId, int TargetSettlementId, CompensationOffer? Offer);
public enum AttackRequestOutcome { Accepted, Refused, AlreadyAgreed }
public sealed record AttackRequestResolvedEvent(GameClock Clock, Guid TaskId, int RequestedForceId,
    int TargetSettlementId, CompensationOffer? Offer, AttackRequestOutcome Outcome, uint Received, uint Spent) : DomainEvent;

internal static class AttackRequestDiplomacy
{
    internal const string Kind = "diplomacy-attack-request";
    internal static bool Target(WorldMutation world, int requester, int requested, int place)
    {
        if (!GiftDiplomacy.Target(world, requester, requested) || !world.Settlements.TryGetValue(place, out var target) ||
            target.IsDestroyed || target.OwnerId is not int owner || owner == requester || owner == requested ||
            world.Forces[owner].IsEliminated) return false;
        if (world.Simulation.Alliance is not { } alliance) return true;
        bool Member(int id) => alliance.MemberForceIds.Contains(id);
        return !(Member(requester) && requested == alliance.TargetForceId || Member(requested) && requester == alliance.TargetForceId ||
            Member(owner) && (Member(requester) || Member(requested)));
    }

    internal static bool Duplicate(WorldMutation world, int requester, int requested, int target) =>
        world.Simulation.AttackRequests.Any(r => r.RequesterForceId == requester && r.RequestedForceId == requested && r.TargetSettlementId == target);

    internal static CommandRejection Eligibility(WorldMutation world, int force, int actor, int target, int place, SimulationRules rules)
    {
        if (!Target(world, force, target, place)) return CommandRejection.InvalidTask;
        if (!PersonnelMissions.CanAct(world, force, actor)) return CommandRejection.PersonUnavailable;
        var origin = world.Settlements[world.Officers[actor].SettlementId!.Value];
        if (ResourceAccounts.Resolve(world, force, origin.CorpsId).Kind != ResourceOwnerKind.Force) return CommandRejection.InvalidCorps;
        return rules.Personnel.Places.ContainsKey(origin.Id) && GiftDiplomacy.Destination(world, target, rules, out _, out _)
            ? CommandRejection.None : CommandRejection.NoDestination;
    }

    internal static bool CanFulfill(WorldMutation world, int source, int requested, int target, SimulationRules rules)
    {
        int? City(SettlementState place) => place.Kind == SettlementKind.City ? place.Id :
            rules.Military is { } map && map.TryPlace(place, out var location) && map.RegionAt(location.Position) is int region &&
                rules.Personnel.RegionCities.TryGetValue(region, out int geographicCity) ? geographicCity :
            rules.Personnel.RegionCities.TryGetValue(place.Id, out int city) ? city : null;
        WorldPosition? Position(SettlementState place) => rules.Military is { } map && map.TryPlace(place, out var location)
            ? location.Position : rules.Personnel.Places.TryGetValue(place.Id, out var personPlace) ? personPlace.Position : null;
        if (world.Simulation.Alliance?.Protects(source, requested) == true ||
            world.Simulation.AttackRequests.Any(r => r.RequestedForceId == requested ||
                r.RequestedForceId == world.Settlements[target].OwnerId && r.RequesterForceId == requested) ||
            City(world.Settlements[target]) is not int targetCity || Position(world.Settlements[target]) is not { } targetPosition) return false;
        var places = world.Settlements.Values.Where(s => s.OwnerId == requested && !s.IsDestroyed).ToArray();
        if (!places.Any(s => City(s) is int city && (city == targetCity ||
            rules.Personnel.CityConnections.Any(c => c.FromCityId == targetCity && c.ToCityId == city)))) return false;
        var nearest = places.Where(s => s.Kind == SettlementKind.City && Position(s) is not null)
            .OrderBy(s => MapCoordinates.Distance(Position(s)!.Value, targetPosition))
            .ThenBy(s => s.Id).FirstOrDefault();
        if (nearest is null || nearest.Soldiers < 10000) return false;
        if (rules.Military is { } military && world.Units.Values.Any(u => !u.IsEmbedded && !u.IsPersonnel &&
            MilitaryDiplomacy.Relation(world, u.OwnerId, requested) < 40 && u.OwnerId != requested &&
            military.RegionAt(u.Position) is int region && rules.Personnel.RegionCities.TryGetValue(region, out int city) && city == nearest.Id)) return false;
        return (long)world.Forces[requested].Food + world.Corps.Values.Where(c => c.OwnerId == requested).Sum(c => (long)(c.Treasury?.Food ?? 0)) >= 1000;
    }

    internal static int FreeThreshold(WorldMutation world, int source, int actor, int target, int place, SimulationRules rules) =>
        CanFulfill(world, source, target, place, rules) && PrisonerCompensation.ExchangeCandidate(world, source, target) is null &&
        MilitaryDiplomacy.Relation(world, target, world.Settlements[place].OwnerId!.Value) < 40 &&
        MilitaryDiplomacy.Relation(world, source, target) >= 80
            ? DiplomacyMath.FreeAttackRequestThreshold(world.Abilities(world.Officers[actor]).Politics,
                MilitaryDiplomacy.Relation(world, source, target)) : 0;

    internal static int PrisonerThreshold(WorldMutation world, int source, int actor, int target, int place, SimulationRules rules) =>
        PrisonerCompensation.ExchangeCandidate(world, source, target) is null ? 0 :
        world.Officers[world.Forces[target].LeaderId!.Value].Righteousness < 6 ? 100 :
        CanFulfill(world, source, target, place, rules) ? (int)Math.Min(int.MaxValue,
            (long)world.Abilities(world.Officers[actor]).Politics + MilitaryDiplomacy.Relation(world, source, target)) : 0;

    internal static CompensationOffer PaymentQuote(WorldMutation world, int source, int actor, int target, int place, SimulationRules rules)
    {
        var refused = new CompensationOffer(CompensationKind.Refuse, 0, null, null);
        var ruler = world.Officers[world.Forces[target].LeaderId!.Value];
        int relation = MilitaryDiplomacy.Relation(world, source, target);
        if (ruler.Righteousness >= 6 && (!CanFulfill(world, source, target, place, rules) || relation < 60)) return refused;
        long quote = 10L * (300L - relation - world.Abilities(world.Officers[actor]).Politics);
        if (quote > 0 && quote <= 1000000 && world.Forces[source].Money > quote)
            return new(CompensationKind.Money, (uint)quote, null, null);
        if (world.Forces[source].LeaderId is not int owner || !world.Officers[owner].IsServing) return refused;
        var items = DiplomacyTreasures.Candidates(world.Treasures.Values, rules.Treasures, owner, ruler.Id);
        return items.IsEmpty ? refused : new(CompensationKind.Treasure, 0, null, items[0]);
    }

    internal static CompensationOffer Quote(WorldMutation world, PersistentTask task, SimulationRules rules)
    {
        var mission = task.AttackRequest!;
        int roll = task.Journey!.SuccessRoll!.Value;
        if (roll < FreeThreshold(world, task.ForceId, task.OfficerId, mission.TargetForceId, mission.TargetSettlementId, rules))
            return new(CompensationKind.Free, 0, null, null);
        if (roll < PrisonerThreshold(world, task.ForceId, task.OfficerId, mission.TargetForceId, mission.TargetSettlementId, rules))
            return new(CompensationKind.Prisoner, 0, PrisonerCompensation.ExchangeCandidate(world, task.ForceId, mission.TargetForceId), null);
        return PaymentQuote(world, task.ForceId, task.OfficerId, mission.TargetForceId, mission.TargetSettlementId, rules);
    }

    internal static bool Accept(WorldMutation world, PersistentTask task, SimulationRules rules)
    {
        var mission = task.AttackRequest!;
        var offer = mission.Offer!;
        int relation = MilitaryDiplomacy.Relation(world, task.ForceId, mission.TargetForceId);
        var target = world.Forces[mission.TargetForceId];
        return offer.Kind switch
        {
            CompensationKind.Free or CompensationKind.Prisoner => true,
            CompensationKind.Money => offer.Money <= 4000L - world.Officers[target.LeaderId!.Value].Righteousness *
                ((long)world.Abilities(world.Officers[task.OfficerId]).Politics + relation) + 2L * target.BaseTrust &&
                offer.Money < world.Forces[task.ForceId].Money / 2 && (long)offer.Money < world.Forces[task.ForceId].Money -
                    PrisonerReturnDiplomacy.SalaryReserve(world, task.ForceId, rules),
            CompensationKind.Treasure => task.Journey!.SuccessRoll >= FreeThreshold(world, task.ForceId, task.OfficerId,
                mission.TargetForceId, mission.TargetSettlementId, rules) && rules.Treasures.Items[offer.TreasureId!.Value].LoyaltyBonus < (relation - 40) / 2,
            _ => false
        };
    }

    internal static CommandAdvice Advice(WorldMutation world, int force, int actor, int target, int place, SimulationRules rules)
    {
        var rejection = Eligibility(world, force, actor, target, place, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        if (PrisonerReturnDiplomacy.Player(world, target)) return new(CommandRejection.None, null, AdviserOpinion.Unavailable);
        int threshold = Math.Max(FreeThreshold(world, force, actor, target, place, rules), PrisonerThreshold(world, force, actor, target, place, rules));
        if (PaymentQuote(world, force, actor, target, place, rules).Kind != CompensationKind.Refuse) threshold = 100;
        return AdviserJudgment.Evaluate(world, force, Kind, actor, target, place, threshold);
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, RequestAttackCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.TargetForceId, command.TargetSettlementId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        GiftDiplomacy.Destination(world, command.TargetForceId, rules, out int destination, out var position);
        int days = DiplomacyJourney.Days(rules.Personnel.Places[origin].Position,
            position ?? rules.Personnel.Places[destination].Position);
        var due = world.Clock;
        for (int i = 0; i < days; i++) due = due.NextDay();
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId, actor.Id, origin, world.Clock, due, 0, command.Sequence)
        {
            Journey = new(destination, null, ConditionalRandom.Percentile(world.Simulation.InitialSeed, ConditionalRandomPurpose.AttackRequest,
                world.Clock, actor.Id, command.TargetForceId, command.TargetSettlementId)) { DestinationPosition = position },
            AttackRequest = new(command.TargetForceId, command.TargetSettlementId, null)
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    internal static bool OfferValid(WorldMutation world, PersistentTask task, CompensationOffer offer, SimulationRules rules) =>
        offer.IsValid && (offer.Kind == CompensationKind.Refuse || PrisonerCompensation.Payable(world, task.ForceId,
            task.AttackRequest!.TargetForceId, offer, rules.Treasures) &&
            (offer.PrisonerId is not int prisoner || PrisonerReturnDiplomacy.CanRecover(world, prisoner, rules)));

    internal static void Finish(WorldMutation world, PersistentTask task, bool accept, SimulationRules rules, List<DomainEvent> events)
    {
        var mission = task.AttackRequest!;
        uint received = 0, spent = 0;
        var outcome = AttackRequestOutcome.AlreadyAgreed;
        if (!Duplicate(world, task.ForceId, mission.TargetForceId, mission.TargetSettlementId))
        {
            outcome = accept ? AttackRequestOutcome.Accepted : AttackRequestOutcome.Refused;
            if (accept)
            {
                AttackRequests.Establish(world, mission.TargetForceId, task.ForceId, mission.TargetSettlementId, events);
                (received, spent) = DiplomacySettlement.Pay(world, task, mission.TargetForceId, mission.Offer!, rules, "attack-request-compensation", events);
                ForceRelations.Set(world, task.ForceId, mission.TargetForceId,
                    DiplomacyMath.AttackRequestAcceptedRelation(MilitaryDiplomacy.Relation(world, task.ForceId, mission.TargetForceId)), events);
                EconomicCycle.UpdateTrust(world, rules.Economy, events);
                DiplomacySettlement.Grow(world, task.OfficerId, events);
            }
            else ForceRelations.Change(world, task.ForceId, mission.TargetForceId, -5, events);
        }
        events.Add(new AttackRequestResolvedEvent(world.Clock, task.Id, mission.TargetForceId, mission.TargetSettlementId,
            mission.Offer, outcome, received, spent));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);
    }
}

internal sealed class AttackRequestRule(SimulationRules rules) : TaskRule
{
    internal override TaskResolutionPhase Phase => TaskResolutionPhase.XunBoundary;
    internal override bool SupportsResolutionStep(int step) => step is 0 or 1;
    internal override bool HasValidPayload(PersistentTask task) => task.Gift is null && task.PrisonerReturn is null && task.Reinforcements is null &&
        task.AttackRequest is { TargetForceId: >= 0, TargetSettlementId: >= 0 } mission && mission.TargetForceId != task.ForceId &&
        (task.ResolutionStep == 0 ? mission.Offer is null : mission.Offer is { IsValid: true }) && task.Journey is
        { TargetOfficerId: null, SuccessRoll: >= 0 and < 100, ReturnLeg: null, AcceptRecruitment: null,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null } && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => DiplomacyJourney.Duration(task, rules);
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) &&
        AttackRequestDiplomacy.Target(world, task.ForceId, task.AttackRequest!.TargetForceId, task.AttackRequest.TargetSettlementId) &&
        (AttackRequestDiplomacy.Duplicate(world, task.ForceId, task.AttackRequest.TargetForceId, task.AttackRequest.TargetSettlementId) ||
            task.AttackRequest.Offer is not { } offer || AttackRequestDiplomacy.OfferValid(world, task, offer, rules));
    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task)
    {
        var mission = task.AttackRequest!;
        if (AttackRequestDiplomacy.Duplicate(world, task.ForceId, mission.TargetForceId, mission.TargetSettlementId) ||
            task.ResolutionStep == 1 && mission.Offer!.Kind is CompensationKind.Free or CompensationKind.Refuse) return null;
        int force = task.ResolutionStep == 0 ? mission.TargetForceId : task.ForceId;
        return PrisonerReturnDiplomacy.Player(world, force) ? force : null;
    }
    internal override bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) =>
        task.ResolutionStep == 1 ? response.Offer is null : !response.Accept ? response.Offer is null :
        response.Offer is { } offer && AttackRequestDiplomacy.OfferValid(world, task, offer, rules);
    internal override PersistentTask? ResolveResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response, List<DomainEvent> events) =>
        task.ResolutionStep == 0 ? Quote(task, response.Accept ? response.Offer! : new(CompensationKind.Refuse, 0, null, null), world.Clock, events)
            : Resolve(world, task, response.Accept, events);
    internal override PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
    {
        var mission = task.AttackRequest!;
        if (AttackRequestDiplomacy.Duplicate(world, task.ForceId, mission.TargetForceId, mission.TargetSettlementId))
        { AttackRequestDiplomacy.Finish(world, task, false, rules, events); return null; }
        if (task.ResolutionStep == 0) return Quote(task, AttackRequestDiplomacy.Quote(world, task, rules), world.Clock, events);
        bool accepted = mission.Offer!.Kind == CompensationKind.Free || mission.Offer.Kind != CompensationKind.Refuse &&
            (accept ?? AttackRequestDiplomacy.Accept(world, task, rules));
        AttackRequestDiplomacy.Finish(world, task, accepted, rules, events);
        return null;
    }
    private static PersistentTask Quote(PersistentTask task, CompensationOffer offer, GameClock clock, List<DomainEvent> events)
    {
        events.Add(new CompensationQuotedEvent(clock, task.Id, offer));
        return task with { AttackRequest = task.AttackRequest! with { Offer = offer } };
    }
}
