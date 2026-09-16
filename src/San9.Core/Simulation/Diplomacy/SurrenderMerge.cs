using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ForceSurrenderedEvent(GameClock Clock, int ForceId, int RecipientId, long MoneyRemoved,
    uint MoneyReceived, long FoodRemoved, uint FoodReceived, ImmutableArray<int> JoinedOfficerIds, ImmutableArray<int> DepartedOfficerIds) : DomainEvent;
public sealed record SettlementTransferredEvent(GameClock Clock, int SettlementId, int PreviousOwnerId, int OwnerId) : DomainEvent;
public sealed record UnitTransferredEvent(GameClock Clock, int UnitId, int PreviousOwnerId, int OwnerId) : DomainEvent;

internal static class SurrenderMerge
{
    internal static void Apply(WorldMutation world, PersistentTask cause, int force, SimulationRules rules, List<DomainEvent> events)
    {
        int recipient = cause.ForceId;
        int? root = world.Corps.Values.Where(c => c.OwnerId == recipient && c.Treasury is null).Select(c => (int?)c.Id).SingleOrDefault();
        // Settle mission reservations before taking the surrender treasury totals.
        foreach (var task in world.Simulation.Tasks.Where(t => t.ForceId == force || t.Gift?.TargetForceId == force ||
            t.PrisonerReturn?.TargetForceId == force || t.AttackRequest?.TargetForceId == force || t.Surrender?.TargetForceId == force)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToArray())
        {
            TaskCancellation.Apply(world, task, "force-surrendered", events);
            if (task.ForceId == force) ReinforcementTransport.Disband(world, task, "force-surrendered", events);
            else if (task.Journey is not null) PersonnelReturns.Start(world, task, rules.Personnel, events, true);
        }
        var oldUnits = world.Units.Values.Where(u => u.OwnerId == force).OrderBy(u => u.Id).ToArray();
        int Place(OfficerState person) => person.SettlementId ?? (person.UnitId is int unit &&
            rules.Military?.RegionAt(world.Units[unit].Position) is int region ? region :
            person.RegionId ?? world.Officers[cause.OfficerId].SettlementId!.Value);
        var people = world.Officers.Values.Where(p => p.OwnerId == force).OrderBy(p => p.Id).ToArray();
        var joined = people.Where(p => p.IsServing && world.NextRandom("surrender-person-join") % 100 < (ulong)p.Loyalty)
            .Select(p => p.Id).ToHashSet();
        var departed = people.Where(p => p.IsServing && !joined.Contains(p.Id)).Select(p => p.Id).ToImmutableArray();
        foreach (var person in people)
        {
            PersonnelTransitions.CancelMissions(world, person.Id, "force-surrendered", events, rules.Personnel, true);
            if (joined.Contains(person.Id))
            {
                int loyalty = 75 + (50 - PersonnelRulebook.AffinityDistance(person, world.Officers[world.Forces[recipient].LeaderId!.Value])) / 2;
                RecruitmentRules.SetLoyalty(world, person.Id, recipient, loyalty, "force-surrendered", events);
                world.Replace(world.Officers[person.Id] with { OwnerId = recipient, Status = OfficerStatus.Serving,
                    OfficeId = null, TacticCooldown = 0, IsCommander = false, Acted = true, StatusStartYear = world.Clock.Year });
                events.Add(new PersonAffiliationChangedEvent(world.Clock, person.Id, force, recipient, Place(person)));
            }
            else if (person.Status == OfficerStatus.Captive)
            {
                // A prisoner already held by another force keeps detention but loses the extinct allegiance.
                world.Replace(world.Officers[person.Id] with { OwnerId = null, OfficeId = null, IsCommander = false });
                events.Add(new PersonAffiliationChangedEvent(world.Clock, person.Id, force, null, Place(person)));
            }
            else if (!person.IsServing)
            {
                world.Replace(world.Officers[person.Id] with { OwnerId = null, OfficeId = null, IsCommander = false });
                events.Add(new PersonAffiliationChangedEvent(world.Clock, person.Id, force, null, Place(person)));
            }
            else
            {
                int increase = Math.Min(person.LoyaltyTo(recipient) - 45, 5);
                if (increase > 0) RecruitmentRules.SetLoyalty(world, person.Id, recipient,
                    Math.Min(255, person.LoyaltyTo(recipient) + increase), "surrender-departure", events);
                PersonnelTransitions.DiscoverDetached(world, person.Id, Place(person), events);
            }
        }
        world.Replace(world.Forces[force] with { LeaderId = null, AdviserId = null });
        foreach (var place in world.Settlements.Values.Where(s => s.OwnerId == force).OrderBy(s => s.Id).ToArray())
        {
            world.Replace(place with { OwnerId = recipient, CorpsId = root });
            events.Add(new SettlementTransferredEvent(world.Clock, place.Id, force, recipient));
        }
        foreach (var original in oldUnits)
        {
            FacilityConstruction.Cancel(world, original.Id, events);
            var unit = world.Units[original.Id];
            var members = unit.MemberOrder.Where(joined.Contains).Concat(world.Officers.Values.Where(p => p.UnitId == unit.Id && p.IsServing)
                .Select(p => p.Id).Order()).Distinct().ToImmutableArray();
            if (members.Length == 0)
            {
                foreach (var prisoner in world.Officers.Values.Where(p => p.UnitId == unit.Id).OrderBy(p => p.Id).ToArray())
                {
                    int origin = Place(prisoner);
                    if (prisoner.Status == OfficerStatus.Captive && rules.Personnel.Places.ContainsKey(origin) &&
                        PrisonerRelease.Depart(world, prisoner.Id, origin, cause.Id, cause.Sequence, PrisonerDepartureKind.Released,
                            force, rules.Personnel, rules.Economy, events, unit.Position) == CommandRejection.None) continue;
                    PersonnelTransitions.DiscoverDetached(world, prisoner.Id, origin, events);
                }
                int? city = rules.Military?.RegionAt(unit.Position) is int region ? Demobilization.City(world, region, rules.Personnel) :
                    unit.ServicePlaceId is int home ? Demobilization.City(world, home, rules.Personnel) : null;
                long troops = (long)unit.Soldiers + unit.Wounded;
                if (city is int destination) Demobilization.Return(world, destination, troops, "surrender-unit-dissolved", unit.Id, events);
                foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
                {
                    uint cargo = resource == ResourceKind.Money ? unit.CargoMoney : unit.CargoFood;
                    ResourceAccounts.Change(world, new(ResourceOwnerKind.Force, recipient), resource, cargo, "surrender-cargo", unit.Id, events);
                    ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), resource, "surrender-cargo", recipient, -(long)cargo, -(long)cargo, events);
                }
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers, "surrender-unit-dissolved", recipient,
                    -(long)unit.Soldiers, -(long)unit.Soldiers, events);
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Wounded, "surrender-unit-dissolved", recipient,
                    -(long)unit.Wounded, -(long)unit.Wounded, events);
                world.RemoveUnit(unit.Id);
                MilitaryPolicies.RemoveTarget(world, unit.Id, false, events);
                events.Add(new UnitTerminatedEvent(world.Clock, unit.Id, null, city is null ? troops : 0));
            }
            else
            {
                world.Replace(unit with { OwnerId = recipient, CorpsId = root,
                    MemberOrder = unit.FormationId is null ? [] : members, CommanderId = unit.FormationId is null ? null : members[0],
                    MemberRanks = unit.MemberRanks.Where(p => joined.Contains(p.Key)).ToImmutableDictionary(),
                    Order = null, MovementPlan = null, EngagementTargetId = null, AwaitingPostAction = false });
                events.Add(new UnitTransferredEvent(world.Clock, unit.Id, force, recipient));
            }
        }
        // Prisoners of the recipient held in transferred assets return to service, never remain self-captured.
        foreach (var person in world.Officers.Values.Where(p => p.Status == OfficerStatus.Captive && p.OwnerId == recipient &&
            PrisonerCompensation.Captor(world, p) == recipient).OrderBy(p => p.Id).ToArray())
            DiplomacySettlement.Recover(world, cause, person.Id, rules, events);
        var corps = world.Corps.Values.Where(c => c.OwnerId == force).OrderBy(c => c.Id).ToArray();
        (long money, uint receivedMoney) = Transfer(ResourceKind.Money);
        (long food, uint receivedFood) = Transfer(ResourceKind.Food);
        foreach (var c in corps)
        {
            world.RemoveCorps(c.Id);
            events.Add(new CorpsDissolvedEvent(world.Clock, force, c.Id, root ?? -1));
        }
        ForceLifecycle.Retire(world, force, events);
        // Clear attacks that became attacks on the unit's own force. Normal enemy orders remain intact.
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            bool main = unit.Order?.IsAttack == true && MilitaryDiplomacy.TargetOwner(world, unit.Order) == unit.OwnerId;
            bool temporary = unit.EngagementTargetId is int other && world.Units[other].OwnerId == unit.OwnerId;
            if (main || temporary) world.Replace(unit with { Order = main ? null : unit.Order, MovementPlan = main ? null : unit.MovementPlan,
                EngagementTargetId = temporary ? null : unit.EngagementTargetId, AwaitingPostAction = false });
            if (main) events.Add(new UnitOrderChangedEvent(world.Clock, unit.Id, null, "force-surrendered"));
            if (temporary) events.Add(new UnitEngagementChangedEvent(world.Clock, unit.Id, null, "force-surrendered"));
        }
        EconomicCycle.UpdateTrust(world, rules.Economy, events);
        events.Add(new ForceSurrenderedEvent(world.Clock, force, recipient, money, receivedMoney, food, receivedFood,
            joined.Order().ToImmutableArray(), departed));

        (long, uint) Transfer(ResourceKind resource)
        {
            var accounts = new[] { new ResourceAccount(ResourceOwnerKind.Force, force) }
                .Concat(corps.Where(c => c.Treasury is not null).Select(c => new ResourceAccount(ResourceOwnerKind.Corps, c.Id))).ToArray();
            long total = accounts.Sum(a => (long)ResourceAccounts.Balance(world, a, resource));
            foreach (var account in accounts) ResourceAccounts.Change(world, account, resource,
                -(long)ResourceAccounts.Balance(world, account, resource), "force-surrendered", cause.OfficerId, events);
            var target = new ResourceAccount(ResourceOwnerKind.Force, recipient);
            uint received = (uint)Math.Min(total / 2, ResourceAccounts.Receivable(world, target, resource));
            ResourceAccounts.Change(world, target, resource, total / 2, "force-surrendered", cause.OfficerId, events);
            return (total, received);
        }
    }
}
