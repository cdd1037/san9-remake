using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Commits a voluntary settlement transfer and settles all personnel attached to the old owner.</summary>
internal static class SettlementAffiliation
{
    private const string Reason = "settlement-defected";

    internal static void Transfer(WorldMutation world, SettlementState place, int recipient, OfficerState? commander,
        OfficerState[] present, SettlementDefectionKind kind, SimulationRules rules, List<DomainEvent> events)
    {
        int owner = place.OwnerId!.Value;
        var leader = world.Officers[world.Forces[recipient].LeaderId!.Value];
        var followers = new HashSet<int>();
        if (commander is not null) followers.Add(commander.Id);
        foreach (var person in present.Where(p => p.Id != commander?.Id))
        {
            int chance = person.LoyaltyTo(owner) >= 100 ? 0 :
                (16 - person.Righteousness) * ((world.Forces[recipient].Trust - world.Forces[owner].Trust) / 50 - person.LoyaltyTo(owner) + 100) / 2;
            if ((int)(world.NextRandom("event-settlement-defection-followers") % 100) < chance) followers.Add(person.Id);
        }
        int? root = world.Corps.Values.SingleOrDefault(c => c.OwnerId == recipient && c.Treasury is null)?.Id;
        BattleTreasury.TransferTo(world, owner, place.CorpsId, BattleTreasury.Weight(place),
            ResourceAccounts.Resolve(world, recipient, root), place.Id, Reason, events);
        world.Replace(place with { OwnerId = recipient, CorpsId = root });
        foreach (var unit in world.Units.Values.Where(u => u.IsEmbedded && u.ServicePlaceId == place.Id && u.OwnerId == owner).ToArray())
            world.Replace(unit with { OwnerId = recipient, CorpsId = root, Order = null, MovementPlan = null, EngagementTargetId = null });
        foreach (int id in followers.Order())
        {
            int loyalty = 75 + (50 - PersonnelRulebook.AffinityDistance(world.Officers[id], leader)) / 2;
            PersonnelTransitions.TransferAllegiance(world, id, recipient, loyalty, place.Id, Reason, rules.Personnel, events, false);
        }
        foreach (var person in world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.SettlementId == place.Id)
            .OrderBy(p => p.Id).ToArray())
        {
            var cause = Cause(world, person.Id, owner, place.Id);
            PersonnelReturns.Repatriate(world, person.Id, place.Id, cause, rules.Personnel, events, false);
            world.Replace(world.Officers[person.Id] with { TacticCooldown = 0 });
            AdvanceSequence(world);
        }
        DisplacePrisoners(world, place.Id, owner, rules, events);
        foreach (var task in world.Simulation.Tasks.Where(t => t.OriginSettlementId == place.Id && t.ForceId != recipient ||
            t.Journey is { } journey && journey.DestinationPlaceId == place.Id && t.ForceId != recipient)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            if (!world.Simulation.Tasks.Any(t => t.Id == task.Id)) continue;
            TaskCancellation.Apply(world, task, Reason, events);
            if (task.Journey is not null) PersonnelReturns.Start(world, task, rules.Personnel, events, false);
        }
        events.Add(new SettlementTransferredEvent(world.Clock, place.Id, owner, recipient));
        BattleAftermath.Reconcile(world, new HashSet<int> { owner }, rules, events, false, Reason);
        ForceTransfer.ClearFriendlyOrders(world, Reason, events);
        CorpsDelegations.Cleanup(world, rules.Military, events);
        world.MaintainLeadership(events);
        events.Add(new SettlementDefectedEvent(world.Clock, place.Id, owner, recipient, kind, commander?.Id, followers.Order().ToImmutableArray()));
        ResolutionPipeline.CheckConquest(world, events);
    }

    internal static void DisplacePrisoners(WorldMutation world, int placeId, int previousOwner,
        SimulationRules rules, List<DomainEvent> events)
    {
        foreach (var prisoner in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive && p.SettlementId == placeId)
            .OrderBy(p => p.Id).ToArray())
        {
            var cause = Cause(world, prisoner.Id, previousOwner, placeId);
            if (PrisonerRelease.Depart(world, prisoner.Id, placeId, cause.Id, cause.Sequence,
                PrisonerDepartureKind.Displaced, null, rules.Personnel, rules.Economy, events) == CommandRejection.None)
                AdvanceSequence(world);
            // No geographic destination: an unrelated captive retains custody in the transferred facility.
            // A recovered captive can always use this now-friendly facility as a valid destination.
        }
    }

    private static PersistentTask Cause(WorldMutation world, int person, int owner, int place) => new(
        new Guid(Convert.FromHexString(CanonicalJson.Hash(new { Kind = Reason, world.Clock, OfficerId = person,
            Sequence = world.Simulation.NextSequence })).AsSpan(0, 16)), PersonnelReturns.Kind,
        world.Simulation.Controls.FirstOrDefault(c => c.ForceId == owner)?.ControllerId ?? "system", owner, person, place,
        world.Clock, world.Clock, 0, world.Simulation.NextSequence);

    private static void AdvanceSequence(WorldMutation world) =>
        world.SetSimulation(world.Simulation with { NextSequence = checked(world.Simulation.NextSequence + 1) });
}
