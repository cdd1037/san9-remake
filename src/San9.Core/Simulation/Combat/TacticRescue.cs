using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticPrisonerRescuedEvent(GameClock Clock, BattleSource Source, int ActorId,
    int TacticId, int TargetUnitId, int OfficerId) : DomainEvent;

internal static class TacticRescue
{
    internal static int? Select(WorldMutation world, BattleSource source, TacticRule tactic,
        MilitaryOrder target, int effect, MilitaryRulebook military)
    {
        if (!tactic.CanRescuePrisoners || effect < 10 || target.UnitId is not int id ||
            !world.Units.TryGetValue(id, out var unit) || unit.IsEmbedded ||
            unit.FormationId is not int formation || military.Formations[formation].PreventsTacticRescue)
            return null;
        var captives = world.Officers.Values.Where(p => p.UnitId == id && p.Status == OfficerStatus.Captive)
            .OrderBy(p => p.Id).ToArray();
        if (captives.Length == 0 || !TacticCombat.Roll(world, "military-tactic-rescue", 50)) return null;
        var own = captives.Where(p => p.OwnerId == source.Owner(world)).ToArray();
        var candidates = own.Length > 0 ? own : captives;
        return candidates[(int)(world.NextRandom("military-tactic-rescue-person") % (uint)candidates.Length)].Id;
    }

    internal static void Release(WorldMutation world, int targetUnitId, int officerId, SimulationRules rules, List<DomainEvent> events)
    {
        var origin = world.Units[targetUnitId].Position;
        var person = world.Officers[officerId];
        var home = BattleRetreat.Destination(world, person, origin, null, rules.Military!);
        if (home is not null)
            BattlePersonnel.Escape(world, officerId, home.Id, rules.Personnel, events, origin, rules.Military!);
        else
            BattlePersonnel.Displace(world, officerId, origin, rules.Personnel, rules.Military!, events);
    }
}
