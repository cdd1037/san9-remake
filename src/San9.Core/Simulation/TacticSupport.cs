using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticHealingEvent(GameClock Clock, int SourceUnitId, int TargetUnitId, uint RecoveredSoldiers,
    ImmutableArray<int> HealedOfficers, bool ConditionCleared) : DomainEvent;

internal static class TacticSupport
{
    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            if (world.Simulation.TerminalReason is not null) return;
            if (!world.Units.TryGetValue(id, out var source) || source.IsPersonnel || source.IsEmbedded ||
                source.Condition is UnitCondition.Confused or UnitCondition.Routed || source.FormationId is not int formationId ||
                military.Formations[formationId].Tactics is not { } profile) continue;
            var formation = military.Formations[formationId];
            var members = world.Officers.Values.Where(p => p.IsServing && p.UnitId == id && p.OwnerId == source.OwnerId)
                .OrderBy(p => source.MemberRanks.GetValueOrDefault(p.Id, BattleRank.Middle)).ThenBy(p => p.Id).ToArray();
            var candidates = members.Where(p => p.Injury == 0 && p.TacticCooldown == 0 && p.EquippedTacticId is int tacticId &&
                military.Tactics[tacticId] is var tactic && tactic.Effect is TacticEffect.Inspire or TacticEffect.Heal &&
                tactic.CompatibleFormations.Contains(formationId) && MilitaryTactics.HasTactic(world, source.OwnerId, [p.Id], tacticId, rules.Treasures))
                .Select(p => new { Officer = p, Tactic = military.Tactics[p.EquippedTacticId!.Value] })
                .Select(p => new { p.Officer, p.Tactic, Targets = Targets(world, source, p.Tactic, military) })
                .Where(p => p.Targets.Length > 0).ToArray();
            if (candidates.Length == 0 || !TacticCombat.Roll(world, "military-tactic-support-opportunity",
                Math.Min(100, profile.OpportunityChance + (source.Condition == UnitCondition.HighSpirits ? 5 : 0)))) continue;
            foreach (var candidate in candidates)
            {
                if (!TacticCombat.Roll(world, "military-tactic-support-member", TacticCombat.AllocationChance(
                    source.MemberRanks.GetValueOrDefault(candidate.Officer.Id, BattleRank.Middle), members.Length, false))) continue;
                var actor = candidate.Officer;
                var tactic = candidate.Tactic;
                world.Replace(world.Officers[actor.Id] with { TacticCooldown = tactic.MinimumInterval });
                events.Add(new TacticActivatedEvent(world.Clock, id, false, candidate.Targets[0], false, tactic.Id, [actor.Id], false, 10));
                int proficiency = actor.TacticProficiencies[tactic.Category!.Value];
                int intelligence = world.Abilities(actor).Intelligence;
                foreach (int targetId in candidate.Targets)
                {
                    var target = world.Units[targetId];
                    if (tactic.Effect == TacticEffect.Inspire)
                    {
                        int morale = Math.Min(100, target.Morale + TacticMath.InspireMorale(proficiency, intelligence, tactic.Power));
                        world.Replace(target with { Morale = morale });
                        events.Add(new TacticMoraleChangedEvent(world.Clock, targetId, false, target.Morale, morale, tactic.Id));
                    }
                    else
                    {
                        uint recovered = TacticMath.HealSoldiers(proficiency, intelligence, tactic.Power, target.Wounded,
                            military.Formations[target.FormationId!.Value].SoldierCapacity - target.Soldiers);
                        bool clear = target.Condition != UnitCondition.Normal || target.ConditionDaysRemaining > 0;
                        world.Replace(target with { Soldiers = target.Soldiers + recovered, Wounded = target.Wounded - recovered,
                            Condition = UnitCondition.Normal, ConditionDaysRemaining = 0 });
                        var healed = world.Officers.Values.Where(p => p.IsServing && p.UnitId == targetId && p.Injury > 0).OrderBy(p => p.Id).ToArray();
                        foreach (var officer in healed) world.Replace(officer with { Injury = 0 });
                        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, targetId), ResourceKind.Soldiers, "tactic-healing", id, recovered, recovered, events);
                        ResourceAccounts.Report(world, new(ResourceOwnerKind.Unit, targetId), ResourceKind.Wounded, "tactic-healing", id, -(long)recovered, -(long)recovered, events);
                        if (clear) events.Add(new BattleConditionChangedEvent(world.Clock, targetId, false, UnitCondition.Normal, 0));
                        events.Add(new TacticHealingEvent(world.Clock, id, targetId, recovered, healed.Select(p => p.Id).ToImmutableArray(), clear));
                    }
                }
                TacticCombat.Grow(world, members, [actor.Id], tactic, events);
                break;
            }
        }
    }

    private static int[] Targets(WorldMutation world, UnitState source, TacticRule tactic, MilitaryRulebook rules)
    {
        var area = MilitaryCombat.Area(source, rules);
        return world.Units.Values.Where(u => u.OwnerId == source.OwnerId && u.FormationId is int formation &&
            MilitaryCombat.Area(u, rules).DistanceTo(area) <= tactic.EffectRadius &&
            (tactic.Effect == TacticEffect.Inspire ? u.Morale < 80 :
                u.Wounded > 0 && u.Soldiers < rules.Formations[formation].SoldierCapacity || u.Condition != UnitCondition.Normal ||
                u.ConditionDaysRemaining > 0 || world.Officers.Values.Any(p => p.IsServing && p.UnitId == u.Id && p.Injury > 0)))
            .OrderBy(u => u.Id).Select(u => u.Id).ToArray();
    }
}
