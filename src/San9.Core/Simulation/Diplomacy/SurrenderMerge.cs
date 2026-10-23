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
        int? root = world.Corps.Values.Where(c => c.OwnerId == recipient && c.IsRoot).Select(c => (int?)c.Id).SingleOrDefault();
        ForceTransfer.CancelTasks(world, force, rules, "force-surrendered", events, world.CurrentDateCommandsConsumed);
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
            if (!joined.Contains(person.Id))
                PersonnelTransitions.CancelMissions(world, person.Id, "force-surrendered", events, rules.Personnel, world.CurrentDateCommandsConsumed);
            if (joined.Contains(person.Id))
            {
                int loyalty = 75 + (50 - PersonnelRulebook.AffinityDistance(person, world.Officers[world.Forces[recipient].LeaderId!.Value])) / 2;
                PersonnelTransitions.TransferAllegiance(world, person.Id, recipient, loyalty, Place(person),
                    "force-surrendered", rules.Personnel, events, world.CurrentDateCommandsConsumed);
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
        ForceTransfer.Settlements(world, force, recipient, root, events);
        foreach (var original in oldUnits)
        {
            FacilityConstruction.Cancel(world, original.Id, events);
            var unit = world.Units[original.Id];
            var members = world.UnitMembers(unit.Id).Where(p => p.IsServing)
                .Select(p => p.Id).Order().ToArray();
            if (members.Length == 0)
            {
                foreach (var prisoner in world.UnitMembers(unit.Id).OrderBy(p => p.Id).ToArray())
                {
                    int origin = Place(prisoner);
                    if (prisoner.Status == OfficerStatus.Captive && rules.Personnel.Places.ContainsKey(origin) &&
                        PrisonerRelease.Depart(world, prisoner.Id, origin, cause.Sequence, PrisonerDepartureKind.Released,
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
                    ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, recipient, null), resource, cargo, "surrender-cargo", unit.Id, events);
                    ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), resource, "surrender-cargo", recipient, -(long)cargo, -(long)cargo, events);
                }
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Soldiers, "surrender-unit-dissolved", recipient,
                    -(long)unit.Soldiers, -(long)unit.Soldiers, events);
                ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, unit.Id), ResourceKind.Wounded, "surrender-unit-dissolved", recipient,
                    -(long)unit.Wounded, -(long)unit.Wounded, events);
                world.RemoveUnit(unit.Id);
                MilitaryOrders.RemoveTarget(world, unit.Id, false, events);
                events.Add(new UnitTerminatedEvent(world.Clock, unit.Id, null, city is null ? troops : 0)
                    { Target = BattleObjectContext.Capture(unit), Source = null, Reason = UnitTerminationReason.Surrendered });
            }
            else
            {
                world.Replace(unit with { OwnerId = recipient, CorpsId = root,
                    CommanderId = unit.FormationId is null ? null : LeadershipMaintenance.SelectUnitCommander(world, members, rules.Governance),
                    MemberRanks = unit.MemberRanks.Where(p => joined.Contains(p.Key)).ToImmutableDictionary(),
                    EngagementTargetId = null, ContactTarget = null, Lookahead = null });
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
        foreach (var unit in oldUnits) MilitaryPolicies.AfterTransfer(world, unit.Id, rules.Military, events, force);
        ForceTransfer.ReconcileOrders(world, rules.Military, events);
        events.Add(new ForceSurrenderedEvent(world.Clock, force, recipient, money, receivedMoney, food, receivedFood,
            joined.Order().ToImmutableArray(), departed));

        (long, uint) Transfer(ResourceKind resource) =>
            ForceTransfer.Treasury(world, force, recipient, resource, 2, "force-surrendered", cause.OfficerId, events);
    }
}
