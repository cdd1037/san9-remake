using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class BattleLearning
{
    internal static void DefeatedUnit(WorldMutation world, BattleSource winner, List<DomainEvent> events)
    {
        OfficerBattleMerit.Credit(world, winner, 1, "unit-defeated", events);
        int? commander = winner.IsSettlement ? LeadershipMaintenance.Commander(world, winner.Id)?.Id : world.Units[winner.Id].CommanderId;
        if (commander is int officer)
            OfficerAbilityGrowth.TryIncrease(world, officer, OfficerAbility.Leadership, "military-defeat-growth", events);
    }

    internal static void CapturedSettlement(WorldMutation world, BattleSource winner, MilitaryRulebook military, List<DomainEvent> events)
    {
        if (winner.IsSettlement) return;
        OfficerBattleMerit.Credit(world, winner, 3, "settlement-captured", events);
        var unit = world.Units[winner.Id];
        var members = world.ServingUnitMembers(unit.Id).OrderBy(p => p.Id).ToArray();
        if (members.Length == 0) return;
        int actor = members[(int)(world.NextRandom("military-capture-growth-member") % (uint)members.Length)].Id;
        OfficerAbilityGrowth.TryIncrease(world, actor, OfficerAbility.Leadership, "military-capture-growth", events);
        foreach (var person in members) OfficerTacticGrowth.Increase(world, person.Id, 5, 3, events);
        int formation = unit.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed && military.UnformedFormationId is int unformed
            ? unformed : military.Formation(unit).Id;
        if (military.Formations[formation].CaptureTacticLearningId is int tactic)
            foreach (var person in members)
                OfficerTacticGrowth.TryLearn(world, person.Id, military.Tactics[tactic], "military-capture-learning", events);
    }

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
        if (world.Simulation.NewGame.FormationTrainingEnabled)
            foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
            {
                if (unit.Soldiers == 0 || unit.Morale == 0 || unit.FormationId is not int formationId || world.Simulation.NewGame.MixedTroopsEnabled && unit.Detachments.IsEmpty ||
                    military.Map.IsWater(unit.Position) || unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
                    unit.EffectiveOrderClass != UnitOrderClass.Military || !MilitaryEngagements.Engaged(world, unit, military)) continue;
                foreach (var officer in world.ServingUnitMembers(unit.Id).OrderBy(p => p.Id).ToArray())
                {
                    if (!unit.Detachments.IsEmpty && (!unit.Detachments.TryGetValue(officer.Id, out var detachment) || detachment.Soldiers == 0)) continue;
                    if (officer.FormationProficiencies?.TryGetValue(formationId, out int current) == true)
                    {
                        if (current < FormationKnowledge.MaximumProficiency)
                            world.Replace(officer with { FormationProficiencies = officer.FormationProficiencies.SetItem(formationId, current + 1) });
                    }
                    else if (officer.Id != unit.CommanderId && unit.CommanderId is int commander &&
                        world.Officers.TryGetValue(commander, out var leader) && FormationKnowledge.Knows(leader, formationId))
                    {
                        var next = FormationKnowledge.Practice(officer, formationId);
                        if (!ReferenceEquals(next, officer)) world.Replace(next);
                    }
                }
            }
        if (world.Companion.DisableDailyCombatProficiencyGain) return;
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id))
        {
            if (unit.FormationId is not int id || !military.Formation(unit).GainsDailyCombatProficiency ||
                unit.EffectiveOrderClass != UnitOrderClass.Military || !MilitaryEngagements.Engaged(world, unit, military)) continue;
            foreach (var officer in world.ServingUnitMembers(unit.Id).OrderBy(p => p.Id).ToArray())
            {
                int? category = military.Formation(unit).TravelMode == TravelMode.Water ? 4 :
                    officer.EquippedTacticId is int tactic ? military.Tactics[tactic].Category : null;
                if (category is int value) OfficerTacticGrowth.Increase(world, officer.Id, value, 1, events);
            }
        }
    }

    internal static void Siege(WorldMutation world, int unitId, List<DomainEvent> events)
    {
        if (!world.Units.TryGetValue(unitId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0) return;
        foreach (var officer in world.ServingUnitMembers(unitId).OrderBy(p => p.Id).ToArray())
            OfficerTacticGrowth.Increase(world, officer.Id, 5, 1, events);
    }

    internal static void DestroyedWork(WorldMutation world, int unitId, BattlefieldPlace place, MilitaryRulebook military, List<DomainEvent> events)
    {
        var members = world.ServingUnitMembers(unitId).OrderBy(p => p.Id).ToArray();
        if (members.Length == 0) return;
        var actor = members[(int)(world.NextRandom("military-destruction-growth-member") % (uint)members.Length)];
        OfficerAbilityGrowth.TryIncrease(world, actor.Id, OfficerAbility.Leadership, "military-destruction-growth", events);
        int? learning = place.FacilityTypeId is int type ? military.Facilities[type].DestructionGrowthTacticId : null;
        foreach (var member in members)
        {
            OfficerTacticGrowth.Increase(world, member.Id, 5, 3, events);
            if (learning is int tactic) OfficerTacticGrowth.TryLearn(world, member.Id, military.Tactics[tactic], "military-destruction-learning", events);
        }
    }
}
