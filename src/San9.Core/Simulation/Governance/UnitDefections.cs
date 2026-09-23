using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record UnitDefectedEvent(GameClock Clock, int UnitId, int PreviousForceId, int ForceId,
    int CommanderId, ImmutableArray<int> FollowerIds) : DomainEvent;

internal static class UnitDefections
{
    internal const string RandomStream = "event-unit-defection";

    internal static void Apply(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (!world.Simulation.UnitDefectionsEnabled || world.Simulation.TerminalReason is not null ||
            world.Clock.DayNumber - world.Simulation.Started.DayNumber < 30 || rules.Military is not { } military) return;
        foreach (int id in DefectionPressure.FieldUnits(world).Select(u => u.Id).Order().ToArray())
        {
            if (!world.Units.TryGetValue(id, out var unit) || !DefectionPressure.OrdinaryForce(world, unit.OwnerId) ||
                unit.FormationId is not int formation || !military.Formations.ContainsKey(formation) ||
                unit.CommanderId is not int commanderId || !world.Officers.TryGetValue(commanderId, out var commander) ||
                !commander.IsServing || commander.UnitId != id || commander.OwnerId != unit.OwnerId || commander.Ambition >= 15 ||
                military.RegionAt(unit.Position) is not int region || !rules.Personnel.Places.ContainsKey(region)) continue;
            int owner = unit.OwnerId;
            if (world.Forces[owner].LeaderId == commanderId) continue;
            if (DefectionPressure.BesiegedRecipient(world, new(id, false), commander, rules, RandomStream) is not int recipient) continue;
            var followers = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == owner && p.UnitId == id).OrderBy(p => p.Id)
                .Where(p => p.Id == commanderId || PersonnelDefection.Follows(world, p, recipient, "event-unit-defection-followers"))
                .Select(p => p.Id).ToImmutableArray();
            int? home = world.Settlements.Values.Where(s => s.OwnerId == recipient && s.CanHostPersons &&
                    SettlementDefections.Active(s) && s.Facility?.BuilderUnitId is null && military.TryPlace(s, out _))
                .OrderBy(s => MapCoordinates.Distance(unit.Position, military.Place(s).Position)).ThenBy(s => s.Id)
                .Select(s => (int?)s.Id).FirstOrDefault();
            Guid cause = new(Convert.FromHexString(CanonicalJson.Hash(new { Kind = "unit-defected", world.Clock, UnitId = id,
                world.Simulation.NextSequence })).AsSpan(0, 16));
            UnitAffiliation.Transfer(world, unit, recipient, home, followers, region, cause, false, rules, events);
            BattleAftermath.Reconcile(world, new HashSet<int> { owner }, rules, events, false, "unit-defected");
            ForceTransfer.ClearFriendlyOrders(world, "unit-defected", events);
            CorpsDelegations.Cleanup(world, military, events);
            world.MaintainLeadership(events);
            events.Add(new UnitDefectedEvent(world.Clock, id, owner, recipient, commanderId, followers));
            ResolutionPipeline.CheckConquest(world, events);
            if (world.Simulation.TerminalReason is not null) return;
        }
    }
}
