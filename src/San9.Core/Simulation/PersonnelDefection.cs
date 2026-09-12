using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PersonnelDefection
{
    internal static bool CanTransfer(WorldMutation world, OfficerState target)
    {
        if (!GovernanceMaintenance.CanRemove(world, target.Id)) return false;
        if (!target.IsCommander || !target.IsServing || target.OwnerId is not int owner || target.SettlementId is not int home) return true;
        if (world.Forces[owner].LeaderId is int ruler && world.Officers[ruler].SettlementId == home) return false;
        if (world.Settlements[home].CorpsId is not int id || world.Corps[id].Treasury is not { } treasury) return true;
        var force = world.Forces[owner];
        return world.Settlements.Values.Any(s => s.Id != home && s.CorpsId == id && s.Kind == SettlementKind.City) &&
            world.Officers.Values.Any(p => p.IsServing && p.OwnerId == owner && p.SettlementId is int location &&
                location != home && world.Settlements[location].CorpsId == id) ||
            (ulong)force.Money + treasury.Money <= uint.MaxValue && (ulong)force.Food + treasury.Food <= uint.MaxValue;
    }

    internal static bool Recruit(WorldMutation world, PersistentTask task, PersonnelRulebook places, GovernanceRulebook governance,
        List<DomainEvent> events)
    {
        var target = world.Officers[task.Journey!.TargetOfficerId!.Value];
        int? oldHome = target.SettlementId;
        int? oldCorps = oldHome is int home ? world.Settlements[home].CorpsId : null;
        bool transfersPlace = target.IsCommander && target.IsServing && target.OwnerId is not null && oldHome is not null;
        RecruitmentRules.ChangeRelation(world, task.ForceId, target.OwnerId, transfersPlace ? 5 : 3, events);
        if (!transfersPlace)
        {
            PersonnelTransitions.Recruit(world, target.Id, task.ForceId, task.OriginSettlementId, events, places, true);
        }
        else
        {
            int owner = target.OwnerId!.Value;
            var source = world.Settlements[oldHome!.Value];
            int? corps = world.Corps.Values.SingleOrDefault(c => c.OwnerId == task.ForceId && c.Treasury is null)?.Id;
            var people = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.SettlementId == source.Id).OrderBy(p => p.Id).ToArray();
            var followers = new HashSet<int> { target.Id };
            foreach (var person in people.Where(p => p.Id != target.Id && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null)))
            {
                int chance = person.Loyalty >= 100 ? 0 : (16 - person.Righteousness) *
                    ((world.Forces[task.ForceId].Trust - world.Forces[owner].Trust) / 50 - person.Loyalty + 100) / 2;
                if ((int)(world.NextRandom("personnel-defection-followers") % 100) < chance) followers.Add(person.Id);
            }
            world.Replace(source with { OwnerId = task.ForceId, CorpsId = corps });
            foreach (var unit in world.Units.Values.Where(u => u.OwnerId == owner && u.ServicePlaceId == source.Id && u.IsEmbedded).ToArray())
                world.Replace(unit with { OwnerId = task.ForceId, CorpsId = corps });
            foreach (var person in people)
            {
                if (followers.Contains(person.Id)) PersonnelTransitions.Recruit(world, person.Id, task.ForceId, source.Id, events, places, true);
                else PersonnelReturns.Repatriate(world, person.Id, source.Id, task, places, events);
            }
            events.Add(new PersonnelTerritoryTransferredEvent(world.Clock, source.Id, owner, task.ForceId, target.Id));
        }
        if (oldCorps is int previousCorps && GovernanceMaintenance.RepairCorps(world, previousCorps, governance, events) != CommandRejection.None)
            throw new InvalidOperationException("Validated affiliation transfer failed.");
        if (oldHome is int origin) GovernanceMaintenance.RepairCommander(world, origin, governance, events);
        return transfersPlace;
    }
}

public sealed record PersonnelTerritoryTransferredEvent(GameClock Clock, int PlaceId, int PreviousForceId, int ForceId, int OfficerId) : DomainEvent;
