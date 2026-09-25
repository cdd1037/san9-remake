using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class PersonnelDefection
{
    internal static bool CanTransfer(WorldMutation world, OfficerState target)
    {
        if (!GovernanceMaintenance.CanRemove(world, target.Id)) return false;
        if (target.UnitId is int unitId && world.Units.TryGetValue(unitId, out var unit) && unit.CommanderId == target.Id)
        {
            var previous = world.Forces[unit.OwnerId];
            if (previous.LeaderId is int fieldRuler && world.Officers[fieldRuler].UnitId == unitId) return false;
            if (unit.CorpsId is not int fieldCorps || world.Corps[fieldCorps].IsRoot) return true;
            return world.Settlements.Values.Any(s => s.CorpsId == fieldCorps && s.Kind == SettlementKind.City) &&
                world.Officers.Values.Any(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId != unitId &&
                    PersonnelLocations.Corps(world, p) == fieldCorps) ||
                (ulong)ResourceAccounts.Root(world, previous.Id).Treasury.Money + world.Corps[fieldCorps].Treasury.Money <= uint.MaxValue && (ulong)ResourceAccounts.Root(world, previous.Id).Treasury.Food + world.Corps[fieldCorps].Treasury.Food <= uint.MaxValue;
        }
        if (!target.IsCommander || !target.IsServing || target.OwnerId is not int owner || target.SettlementId is not int home) return true;
        if (world.Forces[owner].LeaderId is int ruler && world.Officers[ruler].SettlementId == home) return false;
        if (world.Settlements[home].CorpsId is not int id || world.Corps[id].IsRoot) return true;
        var force = world.Forces[owner];
        return world.Settlements.Values.Any(s => s.Id != home && s.CorpsId == id && s.Kind == SettlementKind.City) &&
            world.Officers.Values.Any(p => p.IsServing && p.OwnerId == owner && p.SettlementId is int location &&
                location != home && world.Settlements[location].CorpsId == id) ||
            (ulong)ResourceAccounts.Root(world, force.Id).Treasury.Money + world.Corps[id].Treasury.Money <= uint.MaxValue && (ulong)ResourceAccounts.Root(world, force.Id).Treasury.Food + world.Corps[id].Treasury.Food <= uint.MaxValue;
    }

    internal static bool Recruit(WorldMutation world, PersistentTask task, SimulationRules rules, List<DomainEvent> events)
    {
        var places = rules.Personnel;
        var governance = rules.Governance;
        var target = world.Officers[task.Journey!.TargetOfficerId!.Value];
        int? oldHome = target.SettlementId;
        int? oldCorps = PersonnelLocations.Corps(world, target);
        bool transfersUnit = target.IsServing && target.UnitId is int fieldId && world.Units[fieldId].CommanderId == target.Id;
        bool transfersPlace = target.IsCommander && target.IsServing && target.OwnerId is not null && oldHome is not null;
        RecruitmentRules.ChangeRelation(world, task.ForceId, target.OwnerId, transfersPlace || transfersUnit ? 5 : 3, events);
        if (transfersUnit)
        {
            var unit = world.Units[target.UnitId!.Value];
            var followers = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
                .OrderBy(p => p.Id).Where(p => p.Id == target.Id || Follows(world, p, task.ForceId)).Select(p => p.Id).ToArray();
            UnitAffiliation.Transfer(world, unit, task.ForceId, task.OriginSettlementId, followers,
                task.Journey!.DestinationPlaceId, task.Id, true, rules, events);
        }
        else if (!transfersPlace)
        {
            PersonnelTransitions.Recruit(world, target.Id, task.ForceId, task.OriginSettlementId, events, places, true);
        }
        else
        {
            int owner = target.OwnerId!.Value;
            var source = world.Settlements[oldHome!.Value];
            int? corps = world.Corps.Values.SingleOrDefault(c => c.OwnerId == task.ForceId && c.IsRoot)?.Id;
            var people = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.SettlementId == source.Id).OrderBy(p => p.Id).ToArray();
            var followers = new HashSet<int> { target.Id };
            foreach (var person in people.Where(p => p.Id != target.Id && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null)))
                if (Follows(world, person, task.ForceId)) followers.Add(person.Id);
            world.Replace(source with { OwnerId = task.ForceId, CorpsId = corps });
            foreach (var unit in world.Units.Values.Where(u => u.OwnerId == owner && u.ServicePlaceId == source.Id && u.IsEmbedded).ToArray())
                world.Replace(unit with { OwnerId = task.ForceId, CorpsId = corps });
            foreach (var person in people)
            {
                if (followers.Contains(person.Id)) PersonnelTransitions.Recruit(world, person.Id, task.ForceId, source.Id, events, places, true);
                else PersonnelReturns.Repatriate(world, person.Id, source.Id, task, places, events);
            }
            SettlementAffiliation.DisplacePrisoners(world, source.Id, owner, rules, events);
            events.Add(new PersonnelTerritoryTransferredEvent(world.Clock, source.Id, owner, task.ForceId, target.Id));
        }
        if (oldCorps is int previousCorps && GovernanceMaintenance.RepairCorps(world, previousCorps, governance, events) != CommandRejection.None)
            throw new InvalidOperationException("Validated affiliation transfer failed.");
        if (transfersPlace || target.UnitId is not null)
        {
            BattleAftermath.Reconcile(world, new HashSet<int> { target.OwnerId!.Value }, rules, events, true, "personnel-recruited");
            ForceTransfer.ClearFriendlyOrders(world, "personnel-recruited", events);
            world.MaintainLeadership(events);
            ResolutionPipeline.CheckConquest(world, events);
        }
        return transfersPlace || transfersUnit;
    }

    internal static bool Follows(WorldMutation world, OfficerState person, int recipient,
        string stream = "personnel-defection-followers")
    {
        int chance = person.Loyalty >= 100 ? 0 : (16 - person.Righteousness) *
            ((world.Forces[recipient].Trust - world.Forces[person.OwnerId!.Value].Trust) / 50 - person.Loyalty + 100) / 2;
        return (int)(world.NextRandom(stream) % 100) < chance;
    }


}

public sealed record PersonnelTerritoryTransferredEvent(GameClock Clock, int PlaceId, int PreviousForceId, int ForceId, int OfficerId) : DomainEvent;
