using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class DiplomacySettlement
{
    internal static (uint Received, uint Spent) Pay(WorldMutation world, PersistentTask task, int targetForce,
        CompensationOffer offer, SimulationRules rules, string reason, List<DomainEvent> events)
    {
        uint received = 0;
        if (offer.PrisonerId is int prisoner) Recover(world, task, prisoner, rules, events);
        if (offer.Kind == CompensationKind.Money)
        {
            var source = new ResourceAccount(ResourceOwnerKind.Force, task.ForceId);
            var target = new ResourceAccount(ResourceOwnerKind.Force, targetForce);
            received = Math.Min(offer.Money, ResourceAccounts.Receivable(world, target, ResourceKind.Money));
            ResourceAccounts.Change(world, source, ResourceKind.Money, -(long)offer.Money, reason, task.OfficerId, events, true);
            ResourceAccounts.Change(world, target, ResourceKind.Money, offer.Money, reason, task.OfficerId, events);
        }
        if (offer.Kind == CompensationKind.Treasure)
            PersonnelTreasures.Transfer(world, world.Treasures[offer.TreasureId!.Value], world.Forces[targetForce].LeaderId!.Value,
                task.ForceId, reason, events);
        return (received, offer.Money - received);
    }

    internal static void Recover(WorldMutation world, PersistentTask task, int id, SimulationRules rules, List<DomainEvent> events)
    {
        var person = world.Officers[id];
        var position = person.UnitId is int unit ? world.Units[unit].Position : (WorldPosition?)null;
        int origin = person.SettlementId ?? rules.Military!.RegionAt(position!.Value)!.Value;
        var result = PrisonerRelease.Depart(world, id, origin, task.Id, task.Sequence,
            PrisonerDepartureKind.Recovered, null, rules.Personnel, rules.Economy, events, position);
        if (result != CommandRejection.None) throw new InvalidOperationException("Prisoner recovery failed after validation.");
    }

    internal static void Grow(WorldMutation world, int officer, List<DomainEvent> events)
    {
        var actor = world.Officers[officer];
        if (actor.BaseAbilities.Politics < 100 && world.NextRandom("diplomacy-politics-growth") % 100 < 3)
        {
            world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Politics = actor.BaseAbilities.Politics + 1 } });
            events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "politics", actor.BaseAbilities.Politics, actor.BaseAbilities.Politics + 1));
        }
    }
}
