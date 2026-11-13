using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class ForceCreation
{
    internal static int Found(WorldMutation world, int cityId, OfficerState founder, OfficerState[] members,
        ForceCreationKind kind, SimulationRules rules, List<DomainEvent> events)
    {
        // Validate/allocate before staging any change; identities are unrelated to display resources.
        int forceId = checked(world.AllForces.Keys.DefaultIfEmpty(-1).Max() + 1);
        int corpsId = checked(world.AllCorps.Keys.DefaultIfEmpty(-1).Max() + 1);
        var city = world.Settlements[cityId];
        int? predecessor = city.OwnerId;
        string reason = kind == ForceCreationKind.EmptyCityUprising ? "empty-city-uprising" : "city-independence";
        if ((kind == ForceCreationKind.EmptyCityUprising) != (predecessor is null))
            throw new ArgumentException("Force creation source does not match its kind.");
        int? rank = rules.Politics.InitialRankId;
        var colorUsage = world.AllForces.Values.GroupBy(f => f.ColorCode).ToDictionary(g => g.Key, g => g.Count());
        int color = Enumerable.Range(0, 50).MinBy(code => colorUsage.GetValueOrDefault(code));
        world.AddForce(new(forceId, 100)
        {
            ColorCode = color,
            BaseTrust = 100, LeaderId = founder.Id, RankId = rank,
            Origin = new(kind, founder.Id, cityId, world.Clock) { PredecessorId = predecessor }
        });
        world.AddCorps(new(corpsId, forceId, founder.Id, new(0, 0)) { IsRoot = true, ControlMode = CorpsControlMode.Automatic });
        MilitaryResearch.CancelAt(world, city.Id, events);
        world.Replace(city with { OwnerId = forceId, CorpsId = corpsId });
        ControlRotation.Replace(world, world.Simulation.Controls.Add(new($"ai:{forceId}", forceId, ControllerKind.Automatic)));
        foreach (var person in members.OrderBy(p => p.Id))
        {
            int loyalty = person.Id == founder.Id ? 100 : 95 - PersonnelRulebook.AffinityDistance(person, founder) / 2 +
                (int)(world.NextRandom(kind == ForceCreationKind.EmptyCityUprising ? "event-force-emergence-loyalty" : "event-city-independence-loyalty") % 5);
            PersonnelTransitions.TransferAllegiance(world, person.Id, forceId, loyalty, cityId, reason, rules.Personnel, events, afterDailyCommands: false);
            world.Replace(world.Officers[person.Id] with
            {
                Status = person.Id == founder.Id ? OfficerStatus.Ruler : OfficerStatus.Serving,
                SettlementId = cityId, RegionId = null, UnitId = null, IsCommander = person.Id == founder.Id
            });
        }
        foreach (var force in world.Forces.Values.Where(f => f.Id != forceId && !f.IsEliminated).OrderBy(f => f.Id))
            world.SetRelation(new(Math.Min(forceId, force.Id), Math.Max(forceId, force.Id), force.Id == predecessor ? 10 + (int)(world.NextRandom("event-city-independence-relation") % 10) : 50));
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId != forceId &&
            (p.IsServing || p.Status == OfficerStatus.Captive) && p.Status != OfficerStatus.Ruler &&
            !world.Forces.Values.Any(f => f.LeaderId == p.Id)).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, forceId, predecessor is int old && person.OwnerId == old ? 45 : 50, reason, events);

        var account = ResourceAccounts.Resolve(world, forceId, null);
        ResourceAccounts.Change(world, account, ResourceKind.Money, 2 * EconomyMath.Revenue(city) + 3000, reason, cityId, events);
        ResourceAccounts.Change(world, account, ResourceKind.Food, 2 * EconomyMath.Harvest(city) + 30000, reason, cityId, events);
        if (predecessor is null)
        {
            uint recruits = city.DraftPopulation / 10;
            long requested = (long)recruits + 10000 - city.Soldiers;
            uint soldiers = Math.Min(recruits + 10000, city.SoldierCapacity);
            world.Replace(world.Settlements[cityId] with { DraftPopulation = city.DraftPopulation - recruits, Soldiers = soldiers,
                Morale = soldiers > 0 ? EconomyMath.RecruitMorale(world.Forces[forceId].Trust) : city.Morale });
            var settlementAccount = new ResourceAccount(ResourceOwnerKind.Settlement, cityId);
            ResourceAccounts.Report(world, settlementAccount, ResourceKind.DraftPopulation, reason, forceId, -(long)recruits, -(long)recruits, events);
            ResourceAccounts.Report(world, settlementAccount, ResourceKind.Soldiers, reason, forceId, requested, (long)soldiers - city.Soldiers, events);
        }
        world.MaintainLeadership(events);
        return forceId;
    }
}
