using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ReturnPrisonerCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    int TargetForceId, int PrisonerId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record PrisonerReturnMission([property: JsonRequired] int TargetForceId,
    [property: JsonRequired] int PrisonerId, [property: JsonRequired] CompensationOffer? Offer);
public sealed record PrisonerReturnPrompt(Guid TaskId, int Step, int RespondingForceId, int RequesterForceId,
    int TargetForceId, int PrisonerId, CompensationOffer? Offer);
public sealed record CompensationQuotedEvent(GameClock Clock, Guid TaskId, CompensationOffer Offer) : DomainEvent;
public sealed record PrisonerReturnResolvedEvent(GameClock Clock, Guid TaskId, int PrisonerId,
    CompensationOffer Offer, bool Accepted, uint Received, uint Spent) : DomainEvent;

internal static class PrisonerReturnDiplomacy
{
    internal const string Kind = "diplomacy-prisoner-return";
    internal static CommandAdvice Advice(WorldMutation world, int force, int actor, int target, int prisoner, SimulationRules rules)
    {
        var rejection = Eligibility(world, force, actor, target, prisoner, rules);
        if (rejection != CommandRejection.None) return new(rejection, null, AdviserOpinion.Unavailable);
        if (Player(world, target)) return new(CommandRejection.None, null, AdviserOpinion.Unavailable);
        bool available = PrisonerCompensation.ExchangeCandidate(world, force, target) is not null ||
            PrisonerCompensation.PaymentQuote(world, force, actor, target, prisoner, rules.Treasures).Kind != CompensationKind.Refuse;
        return AdviserJudgment.Evaluate(world, force, Kind, actor, target, prisoner, available ? 100 : 0);
    }
    internal static bool Player(WorldMutation world, int force) => world.Simulation.Controls.Any(c =>
        c.ForceId == force && c.Kind == ControllerKind.Player);

    internal static CommandRejection Eligibility(WorldMutation world, int force, int actor, int target, int prisoner, SimulationRules rules)
    {
        if (!GiftDiplomacy.Target(world, force, target) || !PrisonerCompensation.IsHeld(world, prisoner, force, target))
            return CommandRejection.InvalidTask;
        if (!PersonnelMissions.CanAct(world, force, actor)) return CommandRejection.PersonUnavailable;
        var origin = world.Settlements[world.Officers[actor].SettlementId!.Value];
        if (ResourceAccounts.Resolve(world, force, origin.CorpsId).Kind != ResourceOwnerKind.Force) return CommandRejection.InvalidCorps;
        if (!rules.Personnel.Places.ContainsKey(origin.Id) || !GiftDiplomacy.Destination(world, target, rules, out _, out _) ||
            !CanRecover(world, prisoner, rules)) return CommandRejection.NoDestination;
        return CommandRejection.None;
    }

    internal static CommandRejection Start(WorldMutation world, CommandContext context, ReturnPrisonerCommand command,
        SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command.IssuerId, command.OfficerId, command.TargetForceId, command.PrisonerId, rules);
        if (rejection != CommandRejection.None) return rejection;
        var actor = world.Officers[command.OfficerId];
        int origin = actor.SettlementId!.Value;
        GiftDiplomacy.Destination(world, command.TargetForceId, rules, out int destination, out var position);
        int days = position is { } field ? Math.Max(0, PersonnelRulebook.JourneyDays(rules.Personnel.Places[origin].Position, field) - 1)
            : rules.Personnel.ResolutionDayOffset(origin, destination, PersonnelJourneyKind.Move);
        var due = world.Clock;
        for (int i = 0; i < days; i++) due = due.NextDay();
        var task = new PersistentTask(command.CommandId, Kind, context.ControllerId, command.IssuerId,
            actor.Id, origin, world.Clock, due, 0, command.Sequence)
        {
            Journey = new(destination, null, null) { DestinationPosition = position },
            PrisonerReturn = new(command.TargetForceId, command.PrisonerId, null)
        };
        world.Replace(actor with { Acted = true });
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Add(task) });
        events.Add(new TaskStartedEvent(task));
        return CommandRejection.None;
    }

    private static int? Origin(WorldMutation world, OfficerState person, SimulationRules rules) =>
        person.SettlementId ?? (person.UnitId is int id ? rules.Military?.RegionAt(world.Units[id].Position) : null);

    internal static bool CanRecover(WorldMutation world, int id, SimulationRules rules) =>
        world.Officers.TryGetValue(id, out var person) && Origin(world, person, rules) is int origin &&
        rules.Personnel.Places.ContainsKey(origin) && (world.Settlements.Values.Any(s => s.OwnerId == person.OwnerId &&
            s.CanHostPersons && s.Durability > 0 && rules.Personnel.Places.ContainsKey(s.Id)) ||
            !world.Forces.Values.Any(f => f.LeaderId == id) && PersonnelExile.Destinations(world, origin, rules.Personnel).Length > 0);

    internal static bool Valid(WorldMutation world, PersistentTask task, SimulationRules rules) =>
        task.PrisonerReturn is { } mission && GiftDiplomacy.Target(world, task.ForceId, mission.TargetForceId) &&
        PrisonerCompensation.IsHeld(world, mission.PrisonerId, task.ForceId, mission.TargetForceId) &&
        CanRecover(world, mission.PrisonerId, rules) && (mission.Offer is not { } offer ||
            offer.Kind == CompensationKind.Refuse || PrisonerCompensation.Payable(world, task.ForceId, mission.TargetForceId, offer, rules.Treasures) &&
            (offer.PrisonerId is not int other || CanRecover(world, other, rules)));

    internal static long SalaryReserve(WorldMutation world, int force, SimulationRules rules)
    {
        long salaries = world.Officers.Values.Where(p => p.IsServing && p.Status != OfficerStatus.Ruler && p.OwnerId == force &&
            ResourceAccounts.Resolve(world, force, PersonnelLocations.Corps(world, p)).Kind == ResourceOwnerKind.Force)
            .Sum(p => (long)(p.OfficeId is int office ? rules.Economy.Salaries.Single(s => s.OfficeId == office).Amount : 1));
        return checked(salaries * DiplomacyMath.PrisonerSalaryReserveXuns(world.Clock.Month, world.Clock.Day));
    }

    internal static bool Accept(WorldMutation world, PersistentTask task, SimulationRules rules)
    {
        var mission = task.PrisonerReturn!;
        var offer = mission.Offer!;
        uint value = PrisonerCompensation.Value(world, task.ForceId, task.OfficerId, mission.TargetForceId, mission.PrisonerId);
        return offer.Kind switch
        {
            CompensationKind.Free or CompensationKind.Prisoner => true,
            CompensationKind.Money => DiplomacyMath.AcceptPrisonerRansom(offer.Money, value,
                world.Forces[task.ForceId].Money, SalaryReserve(world, task.ForceId, rules)),
            CompensationKind.Treasure => DiplomacyMath.AcceptPrisonerTreasure(rules.Treasures.Items[offer.TreasureId!.Value].LoyaltyBonus, value),
            _ => false
        };
    }

    internal static void Finish(WorldMutation world, PersistentTask task, bool accepted, SimulationRules rules, List<DomainEvent> events)
    {
        var mission = task.PrisonerReturn!;
        var offer = mission.Offer!;
        uint received = 0;
        if (accepted)
        {
            // All destinations and ownership were revalidated before entering the effect path.
            Recover(mission.PrisonerId);
            if (offer.PrisonerId is int exchange) Recover(exchange);
            if (offer.Kind == CompensationKind.Money)
            {
                var source = new ResourceAccount(ResourceOwnerKind.Force, task.ForceId);
                var target = new ResourceAccount(ResourceOwnerKind.Force, mission.TargetForceId);
                received = Math.Min(offer.Money, ResourceAccounts.Receivable(world, target, ResourceKind.Money));
                ResourceAccounts.Change(world, source, ResourceKind.Money, -(long)offer.Money, "prisoner-compensation", task.OfficerId, events, true);
                ResourceAccounts.Change(world, target, ResourceKind.Money, offer.Money, "prisoner-compensation", task.OfficerId, events);
            }
            if (offer.Kind == CompensationKind.Treasure)
            {
                var item = world.Treasures[offer.TreasureId!.Value];
                PersonnelTreasures.Transfer(world, item, world.Forces[mission.TargetForceId].LeaderId!.Value,
                    task.ForceId, "prisoner-compensation", events);
            }
            if (offer.Kind == CompensationKind.Free)
            {
                var force = world.Forces[mission.TargetForceId];
                world.Replace(force with { BaseTrust = Math.Min(1000, force.BaseTrust + 2) });
            }
            EconomicCycle.UpdateTrust(world, rules.Economy, events);
            var actor = world.Officers[task.OfficerId];
            if (actor.BaseAbilities.Politics < 100 && world.NextRandom("diplomacy-politics-growth") % 100 < 3)
            {
                world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Politics = actor.BaseAbilities.Politics + 1 } });
                events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "politics", actor.BaseAbilities.Politics, actor.BaseAbilities.Politics + 1));
            }
        }
        else ForceRelations.Change(world, task.ForceId, mission.TargetForceId, -5, events);
        events.Add(new PrisonerReturnResolvedEvent(world.Clock, task.Id, mission.PrisonerId, offer, accepted,
            received, accepted ? offer.Money - received : 0));
        PersonnelReturns.Start(world, task, rules.Personnel, events, true);

        void Recover(int id)
        {
            var person = world.Officers[id];
            var position = person.UnitId is int unit ? world.Units[unit].Position : (WorldPosition?)null;
            var result = PrisonerRelease.Depart(world, id, Origin(world, person, rules)!.Value, task.Id, task.Sequence,
                PrisonerDepartureKind.Recovered, null, rules.Personnel, rules.Economy, events, position);
            if (result != CommandRejection.None) throw new InvalidOperationException("Prisoner recovery failed after validation.");
        }
    }
}

internal sealed class PrisonerReturnRule(SimulationRules rules) : TaskRule
{
    internal override bool SupportsResolutionStep(int step) => step is 0 or 1;
    internal override bool HasValidPayload(PersistentTask task) => task.Gift is null && task.Reinforcements is null &&
        task.PrisonerReturn is { TargetForceId: >= 0, PrisonerId: >= 0 } mission && mission.TargetForceId != task.ForceId &&
        (task.ResolutionStep == 0 ? mission.Offer is null : mission.Offer is { IsValid: true }) && task.Journey is
        { TargetOfficerId: null, SuccessRoll: null, ReturnLeg: null, AcceptRecruitment: null,
          UnitStrategy: null, Burn: null, Seize: null, Gossip: null } && DurationDays(task) >= 0;
    internal override int DurationDays(PersistentTask task) => new GiftRule(rules).DurationDays(task);
    internal override bool Validate(WorldMutation world, PersistentTask task) => HasValidPayload(task) && PrisonerReturnDiplomacy.Valid(world, task, rules);
    internal override int? DecisionRecipient(WorldMutation world, PersistentTask task)
    {
        var mission = task.PrisonerReturn!;
        if (task.ResolutionStep == 1 && mission.Offer!.Kind is CompensationKind.Free or CompensationKind.Refuse) return null;
        int respondent = task.ResolutionStep == 0 ? mission.TargetForceId : task.ForceId;
        return PrisonerReturnDiplomacy.Player(world, respondent) ? respondent : null;
    }
    internal override bool AcceptsResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response) =>
        task.ResolutionStep == 1 ? response.Offer is null : !response.Accept ? response.Offer is null :
        response.Offer is { IsValid: true } offer && (offer.Kind == CompensationKind.Refuse ||
            PrisonerCompensation.Payable(world, task.ForceId, task.PrisonerReturn!.TargetForceId, offer, rules.Treasures) &&
            (offer.PrisonerId is not int prisoner || PrisonerReturnDiplomacy.CanRecover(world, prisoner, rules)));
    internal override PersistentTask? ResolveResponse(WorldMutation world, PersistentTask task, RespondDailyTaskCommand response,
        List<DomainEvent> events) => task.ResolutionStep == 0
        ? Quote(world, task, response.Accept ? response.Offer! : new(CompensationKind.Refuse, 0, null, null), events)
        : Resolve(world, task, response.Accept, events);
    internal override PersistentTask? Resolve(WorldMutation world, PersistentTask task, bool? accept, List<DomainEvent> events)
    {
        var mission = task.PrisonerReturn!;
        if (task.ResolutionStep == 0) return Quote(world, task, PrisonerCompensation.Quote(world, task.ForceId,
            task.OfficerId, mission.TargetForceId, mission.PrisonerId, rules.Treasures), events);
        bool accepted = mission.Offer!.Kind == CompensationKind.Free ||
            mission.Offer.Kind != CompensationKind.Refuse && (accept ?? PrisonerReturnDiplomacy.Accept(world, task, rules));
        PrisonerReturnDiplomacy.Finish(world, task, accepted, rules, events);
        return null;
    }
    private static PersistentTask Quote(WorldMutation world, PersistentTask task, CompensationOffer offer, List<DomainEvent> events)
    {
        events.Add(new CompensationQuotedEvent(world.Clock, task.Id, offer));
        return task with { PrisonerReturn = task.PrisonerReturn! with { Offer = offer } };
    }
}
