using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleLearning
{
    internal static void Learn(WorldMutation world, IEnumerable<OfficerState> members, TacticRule tactic, List<DomainEvent> events)
    {
        foreach (var officer in members.OrderBy(p => p.Id))
            if (world.Officers.TryGetValue(officer.Id, out var current) && current.IsServing)
                OfficerTacticGrowth.TryLearn(world, officer.Id, tactic, "military-tactic-learning", events);
    }

    internal static void Passive(WorldMutation world, IEnumerable<OfficerState> members, TacticEffect effect,
        MilitaryRulebook military, List<DomainEvent> events)
    {
        foreach (var tactic in military.Tactics.Values.Where(t => t.Effect == effect).OrderBy(t => t.Id))
            Learn(world, members, tactic, events);
    }

    internal static void Daily(WorldMutation world, MilitaryRulebook? military, List<DomainEvent> events)
    {
        if (military is null) return;
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id))
        {
            if (unit.FormationId is not int id || !military.Formations[id].GainsDailyCombatProficiency ||
                unit.EffectiveOrderClass != UnitOrderClass.Military || !MilitaryEngagements.Engaged(world, unit, military)) continue;
            foreach (var officer in world.Officers.Values.Where(p => p.IsServing && p.UnitId == unit.Id).OrderBy(p => p.Id).ToArray())
            {
                int? category = military.Formations[id].TravelMode == TravelMode.Water ? 4 :
                    officer.EquippedTacticId is int tactic ? military.Tactics[tactic].Category : null;
                if (category is int value) OfficerTacticGrowth.Increase(world, officer.Id, value, 1, events);
            }
        }
    }

    internal static void Siege(WorldMutation world, int unitId, List<DomainEvent> events)
    {
        if (!world.Units.TryGetValue(unitId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0) return;
        foreach (var officer in world.Officers.Values.Where(p => p.IsServing && p.UnitId == unitId).OrderBy(p => p.Id).ToArray())
            OfficerTacticGrowth.Increase(world, officer.Id, 5, 1, events);
    }

    internal static void DestroyedWork(WorldMutation world, int unitId, BattlefieldPlace place, MilitaryRulebook military, List<DomainEvent> events)
    {
        var members = world.Officers.Values.Where(p => p.IsServing && p.UnitId == unitId).OrderBy(p => p.Id).ToArray();
        if (members.Length == 0) return;
        var actor = members[(int)(world.NextRandom("military-destruction-growth-member") % (uint)members.Length)];
        int before = actor.BaseAbilities.Leadership;
        if (before < 100 && TacticCombat.Roll(world, "military-destruction-growth", 3))
        {
            world.Replace(actor with { BaseAbilities = actor.BaseAbilities with { Leadership = before + 1 } });
            events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, "leadership", before, before + 1));
        }
        int? learning = place.FacilityTypeId is int type ? military.Facilities[type].DestructionGrowthTacticId : null;
        foreach (var member in members)
        {
            OfficerTacticGrowth.Increase(world, member.Id, 5, 3, events);
            if (learning is int tactic) OfficerTacticGrowth.TryLearn(world, member.Id, military.Tactics[tactic], "military-destruction-learning", events);
        }
    }
}
