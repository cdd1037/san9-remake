using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ForceFoundedEvent(GameClock Clock, int ForceId, int SettlementId, int FounderId,
    ImmutableArray<int> MemberIds) : DomainEvent;

internal static class ForceEmergence
{
    private const string Reason = "empty-city-uprising";

    private static IOrderedEnumerable<OfficerState> Order(IEnumerable<OfficerState> people) => people
        .OrderByDescending(p => p.ForceFounderPriority > 0)
        .ThenByDescending(p => p.ForceFounderPriority)
        .ThenByDescending(p => p.ForceFounderPriority == 0 ? p.Ambition : 0).ThenBy(p => p.Id);

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.ForceEmergenceEnabled || world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30) return;
        int? City(OfficerState person)
        {
            if ((person.SettlementId ?? person.RegionId) is not int place) return null;
            if (world.Settlements.TryGetValue(place, out var settlement) && settlement.Kind == SettlementKind.City) return place;
            return rules.Personnel.RegionCities.TryGetValue(place, out int city) ? city : null;
        }
        bool EmptyCity(int? id) => id is int city && world.Settlements.TryGetValue(city, out var place) &&
            place.Kind == SettlementKind.City && place.OwnerId is null && !place.IsDestroyed && place.CanHostPersons && place.Durability > 0;
        var free = world.Officers.Values.Where(p => p.Status == OfficerStatus.Free).ToArray();
        foreach (var candidate in Order(free.Where(p => (p.Ambition > 9 || p.ForceFounderPriority > 0) && EmptyCity(City(p)))))
        {
            int city = City(candidate)!.Value;
            var cities = rules.Personnel.CityConnections.Where(c => c.FromCityId == city).Select(c => c.ToCityId).Append(city).ToHashSet();
            var supporters = free.Where(p => p.Id != candidate.Id && City(p) is int current && cities.Contains(current) &&
                PersonnelRulebook.AffinityDistance(candidate, p) <= 10).ToArray();
            if (supporters.Length < 2) continue;
            var members = Order(supporters.Append(candidate)).ToArray();
            Found(world, city, members[0], members, rules, events);
            return;
        }
    }

    private static void Found(WorldMutation world, int cityId, OfficerState founder, OfficerState[] members,
        SimulationRules rules, List<DomainEvent> events)
    {
        // Validate/allocate before staging any change; identities are unrelated to display resources.
        int forceId = checked(world.Forces.Keys.DefaultIfEmpty(-1).Max() + 1);
        int corpsId = checked(world.Corps.Keys.DefaultIfEmpty(-1).Max() + 1);
        var city = world.Settlements[cityId];
        int? rank = rules.Governance.RankPrecedence.OrderBy(r => r.Value).ThenBy(r => r.Key).Select(r => (int?)r.Key).FirstOrDefault();
        world.AddForce(new(forceId, 0, 0, 100)
        {
            BaseTrust = 100, LeaderId = founder.Id, RankId = rank,
            Origin = new(ForceCreationKind.EmptyCityUprising, founder.Id, cityId, world.Clock)
        });
        world.AddCorps(new(corpsId, forceId, founder.Id, null) { ControlMode = CorpsControlMode.Automatic });
        world.Replace(city with { OwnerId = forceId, CorpsId = corpsId });
        world.SetSimulation(world.Simulation with { Controls = world.Simulation.Controls.Add(new($"ai:{forceId}", forceId, ControllerKind.Automatic)) });
        foreach (var person in members.OrderBy(p => p.Id))
        {
            int loyalty = person.Id == founder.Id ? 100 : 95 - PersonnelRulebook.AffinityDistance(person, founder) / 2 +
                (int)(world.NextRandom("event-force-emergence-loyalty") % 5);
            PersonnelTransitions.TransferAllegiance(world, person.Id, forceId, loyalty, cityId, Reason, rules.Personnel, events);
            world.Replace(world.Officers[person.Id] with
            {
                Status = person.Id == founder.Id ? OfficerStatus.Ruler : OfficerStatus.Serving,
                SettlementId = cityId, RegionId = null, UnitId = null, IsCommander = person.Id == founder.Id
            });
        }
        foreach (var force in world.Forces.Values.Where(f => f.Id != forceId && !f.IsEliminated).OrderBy(f => f.Id))
            world.SetRelation(new(Math.Min(forceId, force.Id), Math.Max(forceId, force.Id), 50));
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId != forceId &&
            (p.IsServing || p.Status == OfficerStatus.Captive) && p.Status != OfficerStatus.Ruler &&
            !world.Forces.Values.Any(f => f.LeaderId == p.Id)).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, forceId, 50, Reason, events);

        EconomicCycle.UpdateTrust(world, rules.Economy, events);
        var account = new ResourceAccount(ResourceOwnerKind.Force, forceId);
        ResourceAccounts.Change(world, account, ResourceKind.Money, 2 * EconomyMath.Revenue(city) + 3000, Reason, cityId, events);
        ResourceAccounts.Change(world, account, ResourceKind.Food, 2 * EconomyMath.Harvest(city) + 30000, Reason, cityId, events);
        uint recruits = city.DraftPopulation / 10;
        long requested = (long)recruits + 10000 - city.Soldiers;
        uint soldiers = Math.Min(recruits + 10000, city.SoldierCapacity);
        world.Replace(world.Settlements[cityId] with { DraftPopulation = city.DraftPopulation - recruits, Soldiers = soldiers,
            Morale = soldiers > 0 ? EconomyMath.RecruitMorale(world.Forces[forceId].Trust) : city.Morale });
        var settlementAccount = new ResourceAccount(ResourceOwnerKind.Settlement, cityId);
        ResourceAccounts.Report(world, settlementAccount, ResourceKind.DraftPopulation, Reason, forceId, -(long)recruits, -(long)recruits, events);
        ResourceAccounts.Report(world, settlementAccount, ResourceKind.Soldiers, Reason, forceId, requested, (long)soldiers - city.Soldiers, events);
        world.MaintainLeadership(events);
        events.Add(new ForceFoundedEvent(world.Clock, forceId, cityId, founder.Id, members.Select(p => p.Id).Order().ToImmutableArray()));
    }
}
