using San9.Core.Domain;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public sealed record DissolveCorpsCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsDissolvedEvent(GameClock Clock, int ForceId, int CorpsId, int DestinationCorpsId) : DomainEvent;
public sealed record CreateCorpsCommand(Guid CommandId, int IssuerId, long Sequence, ImmutableArray<int> CityIds, uint Money, uint Food, [property: JsonRequired] CorpsDelegation Delegation)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsCreatedEvent(GameClock Clock, int ForceId, int CorpsId, int LeaderId, ImmutableArray<int> CityIds) : DomainEvent;
public sealed record ChangeCorpsCommand(Guid CommandId, int IssuerId, long Sequence, int CorpsId,
    ImmutableArray<int> CityIds, uint Money, uint Food, [property: JsonRequired] CorpsDelegation Delegation) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record CorpsChangedEvent(GameClock Clock, int ForceId, int CorpsId, int LeaderId,
    ImmutableArray<int> CityIds) : DomainEvent;

internal static class CorpsGovernance
{
    internal static CommandRejection Change(WorldMutation world, ChangeCorpsCommand command, PersonnelRulebook geography,
        GovernanceRulebook rules, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (!world.Corps.TryGetValue(command.CorpsId, out var corps) || corps.OwnerId != command.IssuerId ||
            corps.IsRoot || command.CityIds.IsDefaultOrEmpty ||
            command.CityIds.Distinct().Count() != command.CityIds.Length) return CommandRejection.InvalidCorps;
        if (!CorpsDelegations.Valid(world, command.IssuerId, command.Delegation, military)) return CommandRejection.InvalidCorps;
        var roots = world.Corps.Values.Where(c => c.OwnerId == command.IssuerId && c.IsRoot).ToArray();
        if (roots.Length != 1) return CommandRejection.InvalidCorps;
        var root = roots[0];
        var force = world.Forces[command.IssuerId];
        if (force.LeaderId is not int rulerId) return CommandRejection.InvalidCorps;
        var ruler = world.Officers[rulerId];
        var cities = command.CityIds.ToHashSet();
        if (cities.Any(id => !world.Settlements.TryGetValue(id, out var city) || city.Kind != SettlementKind.City ||
                city.OwnerId != force.Id || city.CorpsId != root.Id && city.CorpsId != corps.Id) ||
            !world.Settlements.Values.Any(s => s.Kind == SettlementKind.City &&
                (s.CorpsId == root.Id || s.CorpsId == corps.Id) && !cities.Contains(s.Id)) ||
            PersonnelLocations.ServicePlace(world, ruler) is int rulerCity && cities.Contains(rulerCity)) return CommandRejection.InvalidCorps;
        var places = geography.RegionCities.Where(p => cities.Contains(p.Value)).Select(p => p.Key).Concat(cities).ToHashSet();
        var facilities = world.Settlements.Values.Where(s => places.Contains(s.Id) && s.OwnerId == force.Id && s.Id != PersonnelLocations.ServicePlace(world, ruler)).ToArray();
        if (facilities.Any(s => s.CorpsId != root.Id && s.CorpsId != corps.Id)) return CommandRejection.InvalidCorps;
        places = facilities.Select(s => s.Id).ToHashSet();
        var busy = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var leader = rules.SelectGovernor(world.Officers.Values.Where(p => p.OwnerId == force.Id &&
            p.SettlementId is int home && places.Contains(home) && !busy.Contains(p.Id)), world.AbilitiesFor(world.Officers.Values));
        if (leader is null) return CommandRejection.PersonUnavailable;
        long moneyDelta = (long)command.Money - corps.Treasury.Money;
        long foodDelta = (long)command.Food - corps.Treasury.Food;
        if (moneyDelta > root.Treasury.Money || foodDelta > root.Treasury.Food) return CommandRejection.InsufficientFunds;
        if ((long)root.Treasury.Money - moneyDelta > uint.MaxValue || (long)root.Treasury.Food - foodDelta > uint.MaxValue)
            return CommandRejection.ResourceOverflow;

        var removed = world.Settlements.Values.Where(s => s.CorpsId == corps.Id && !places.Contains(s.Id)).Select(s => s.Id).ToHashSet();
        foreach (var facility in world.Settlements.Values.Where(s => removed.Contains(s.Id)).ToArray())
            world.Replace(facility with { CorpsId = root.Id });
        foreach (var facility in facilities) world.Replace(facility with { CorpsId = corps.Id });
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == force.Id &&
            (u.CorpsId == root.Id || u.CorpsId == corps.Id) && u.ServicePlaceId is not null && u.FollowsServicePlace(false)).ToArray())
        {
            if (places.Contains(unit.ServicePlaceId!.Value)) world.Replace(unit with { CorpsId = corps.Id });
            else if (removed.Contains(unit.ServicePlaceId.Value)) world.Replace(unit with { CorpsId = root.Id });
        }
        var oldLeader = world.Officers[corps.LeaderId!.Value];
        if (oldLeader.Id != leader.Id && oldLeader.Status == OfficerStatus.Governor)
            world.Replace(oldLeader with { Status = OfficerStatus.Serving });
        world.Replace(leader with { Status = OfficerStatus.Governor });
        world.Replace(corps with { LeaderId = leader.Id, Treasury = new(command.Money, command.Food), Delegation = command.Delegation });
        world.Replace(force with { AdviserId = force.AdviserId == leader.Id ? null : force.AdviserId });
        world.Replace(root with { Treasury = new((uint)(root.Treasury.Money - moneyDelta), (uint)(root.Treasury.Food - foodDelta)) });
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = resource == ResourceKind.Money ? moneyDelta : foodDelta;
            ResourceAccounts.Report(world, ResourceAccounts.Resolve(world, force.Id, null), resource, "corps-changed", corps.Id, -amount, -amount, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Corps, corps.Id), resource, "corps-changed", root.Id, amount, amount, events);
        }
        if (corps.Delegation != command.Delegation)
            events.Add(new CorpsDelegationChangedEvent(world.Clock, force.Id, corps.Id, corps.Delegation, command.Delegation, "ordered"));
        events.Add(new CorpsChangedEvent(world.Clock, force.Id, corps.Id, leader.Id, cities.Order().ToImmutableArray()));
        return CommandRejection.None;
    }

    internal static CommandRejection Create(WorldMutation world, CreateCorpsCommand command, PersonnelRulebook geography,
        GovernanceRulebook rules, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (!CorpsDelegations.Valid(world, command.IssuerId, command.Delegation, military)) return CommandRejection.InvalidCorps;
        var roots = world.Corps.Values.Where(c => c.OwnerId == command.IssuerId && c.IsRoot).ToArray();
        if (roots.Length != 1 || command.CityIds.IsDefaultOrEmpty || command.CityIds.Distinct().Count() != command.CityIds.Length ||
            world.Corps.Values.Count(c => c.OwnerId == command.IssuerId) >= GovernanceRulebook.MaximumCorpsPerForce ||
            world.AllCorps.Count != 0 && world.AllCorps.Keys.Max() == int.MaxValue) return CommandRejection.InvalidCorps;
        var root = roots[0];
        var force = world.Forces[command.IssuerId];
        if (force.LeaderId is not int rulerId || !world.Officers.TryGetValue(rulerId, out var ruler))
            return CommandRejection.InvalidCorps;
        var cities = command.CityIds.ToHashSet();
        if (cities.Any(id => !world.Settlements.TryGetValue(id, out var city) || city.Kind != SettlementKind.City ||
                city.OwnerId != force.Id || city.CorpsId != root.Id) ||
            !world.Settlements.Values.Any(s => s.Kind == SettlementKind.City && s.CorpsId == root.Id && !cities.Contains(s.Id)) ||
            PersonnelLocations.ServicePlace(world, ruler) is int rulerCity && cities.Contains(rulerCity)) return CommandRejection.InvalidCorps;
        var places = geography.RegionCities.Where(p => cities.Contains(p.Value)).Select(p => p.Key).Concat(cities).ToHashSet();
        var facilities = world.Settlements.Values.Where(s => places.Contains(s.Id) && s.OwnerId == force.Id && s.Id != PersonnelLocations.ServicePlace(world, ruler)).ToArray();
        if (facilities.Any(s => s.CorpsId != root.Id)) return CommandRejection.InvalidCorps;
        places = facilities.Select(s => s.Id).ToHashSet();
        var busy = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var leader = rules.SelectGovernor(world.Officers.Values.Where(p => p.OwnerId == force.Id &&
            p.SettlementId is int home && places.Contains(home) && home != PersonnelLocations.ServicePlace(world, ruler) && !busy.Contains(p.Id)), world.AbilitiesFor(world.Officers.Values));
        if (leader is null) return CommandRejection.PersonUnavailable;
        if (command.Money > root.Treasury.Money || command.Food > root.Treasury.Food) return CommandRejection.InsufficientFunds;
        int id = world.AllCorps.Keys.Max() + 1;
        if (force.AdviserId == leader.Id) world.Replace(force = force with { AdviserId = null });
        world.Replace(leader with { Status = OfficerStatus.Governor });
        world.AddCorps(new(id, force.Id, leader.Id, new(command.Money, command.Food)) { ControlMode = CorpsControlMode.Automatic, Delegation = command.Delegation });
        foreach (var facility in facilities) world.Replace(facility with { CorpsId = id });
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == force.Id && u.CorpsId == root.Id &&
            u.ServicePlaceId is int home && places.Contains(home) && u.FollowsServicePlace(true)).ToArray())
            world.Replace(unit with { CorpsId = id });
        world.Replace(root with { Treasury = new(root.Treasury.Money - command.Money, root.Treasury.Food - command.Food) });
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = resource == ResourceKind.Money ? command.Money : command.Food;
            ResourceAccounts.Report(world, ResourceAccounts.Resolve(world, force.Id, null), resource, "corps-created", id, -amount, -amount, events);
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Corps, id), resource, "corps-created", root.Id, amount, amount, events);
        }
        events.Add(new CorpsCreatedEvent(world.Clock, force.Id, id, leader.Id, cities.Order().ToImmutableArray()));
        return CommandRejection.None;
    }

    internal static CommandRejection Dissolve(WorldMutation world, DissolveCorpsCommand command, GovernanceRulebook rules, List<DomainEvent> events)
    {
        if (!world.Corps.TryGetValue(command.CorpsId, out var source) || source.OwnerId != command.IssuerId ||
            source.IsRoot) return CommandRejection.InvalidCorps;
        var roots = world.Corps.Values.Where(c => c.OwnerId == command.IssuerId && c.IsRoot).ToArray();
        if (roots.Length != 1 || source.LeaderId == world.Forces[command.IssuerId].LeaderId)
            return CommandRejection.InvalidCorps;
        var root = roots[0];
        var force = world.Forces[command.IssuerId];
        if ((ulong)root.Treasury.Money + source.Treasury.Money > uint.MaxValue ||
            (ulong)root.Treasury.Food + source.Treasury.Food > uint.MaxValue) return CommandRejection.ResourceOverflow;

        foreach (var settlement in world.Settlements.Values.Where(s => s.CorpsId == source.Id).OrderBy(s => s.Id).ToArray())
            world.Replace(settlement with { CorpsId = root.Id });
        foreach (var unit in world.Units.Values.Where(u => u.CorpsId == source.Id).OrderBy(u => u.Id).ToArray())
            world.Replace(unit with { CorpsId = root.Id });
        var leader = world.Officers[source.LeaderId!.Value];
        if (leader.Status == OfficerStatus.Governor) world.Replace(leader with { Status = OfficerStatus.Serving });
        world.Replace(root with { Treasury = new(root.Treasury.Money + source.Treasury.Money, root.Treasury.Food + source.Treasury.Food) });
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            long amount = resource == ResourceKind.Money ? source.Treasury.Money : source.Treasury.Food;
            ResourceAccounts.Report(world, new(ResourceOwnerKind.Corps, source.Id), resource, "corps-dissolved", root.Id, -amount, -amount, events);
            ResourceAccounts.Report(world, ResourceAccounts.Resolve(world, force.Id, null), resource, "corps-dissolved", source.Id, amount, amount, events);
        }
        world.RemoveCorps(source.Id);
        events.Add(new CorpsDissolvedEvent(world.Clock, force.Id, source.Id, root.Id));
        return CommandRejection.None;
    }
}
