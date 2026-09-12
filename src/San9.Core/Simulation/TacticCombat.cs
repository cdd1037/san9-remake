using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticActivatedEvent(GameClock Clock, int SourceId, bool SourceIsSettlement, int TargetId,
    bool TargetIsSettlement, int TacticId, ImmutableArray<int> OfficerIds, bool Doubled, int Effect) : DomainEvent;
public sealed record TacticProficiencyGrownEvent(GameClock Clock, int OfficerId, int Category, int Previous, int Current) : DomainEvent;
public sealed record TacticLearnedEvent(GameClock Clock, int OfficerId, int TacticId) : DomainEvent;

internal static class TacticCombat
{
    private sealed record Participant(OfficerState Officer, TacticRule Tactic);

    internal static void TryUnit(WorldMutation world, int sourceId, MilitaryOrder target, SimulationRules rules,
        List<DomainEvent> events, bool counter = false)
    {
        var military = rules.Military!;
        if (!world.Units.TryGetValue(sourceId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0 ||
            unit.Condition is UnitCondition.Confused or UnitCondition.Routed || unit.IsEmbedded || unit.IsPersonnel ||
            unit.FormationId is not int formationId || military.Formations[formationId] is not { Tactics: { } profile } formation ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military) ||
            MilitaryCombat.Area(unit, military).DistanceTo(MilitaryCombat.TargetArea(world, target, military)) > formation.Attack!.Range) return;
        var members = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
            .OrderBy(p => unit.MemberRanks.GetValueOrDefault(p.Id, BattleRank.Middle)).ThenBy(p => p.Id).ToArray();
        var candidates = members.Where(p => p.Injury == 0 && p.TacticCooldown == 0)
            .Select(p => new { Officer = p, Tactic = ChooseTactic(world, p, unit.OwnerId, formation, target, rules) })
            .Where(p => p.Tactic is not null).Select(p => new Participant(p.Officer, p.Tactic!)).ToArray();
        if (candidates.Length == 0 || counter && !Roll(world, "military-tactic-counter", profile.CounterChance)) return;
        int chance = Math.Min(100, profile.OpportunityChance + (unit.Condition == UnitCondition.HighSpirits ? 5 : 0));
        if (!Roll(world, "military-tactic-opportunity", chance)) return;
        Participant? primary = null;
        int intelligenceEffect = 0;
        var selection = !profile.AutomaticTactics.IsEmpty
            ? new[] { candidates[(int)(world.NextRandom("military-tactic-member") % (uint)candidates.Length)] } : candidates;
        foreach (var candidate in selection)
        {
            if (profile.AutomaticTactics.IsEmpty && !Roll(world, "military-tactic-member", AllocationChance(unit.MemberRanks[candidate.Officer.Id],
                members.Length, candidate.Tactic.Effect == TacticEffect.Martial && profile.FavoredCategories.Contains(candidate.Tactic.Category!.Value)))) continue;
            if (candidate.Tactic.Effect != TacticEffect.Martial)
            {
                intelligenceEffect = IntelligenceTactics.Effect(world, candidate.Officer, candidate.Tactic, target, rules);
                if (intelligenceEffect == 0) continue;
            }
            primary = candidate;
            break;
        }
        if (primary is null) return;
        if (primary.Tactic.Effect != TacticEffect.Martial)
        {
            world.Replace(world.Officers[primary.Officer.Id] with { TacticCooldown = primary.Tactic.MinimumInterval });
            events.Add(new TacticActivatedEvent(world.Clock, unit.Id, false, target.UnitId ?? target.SettlementId!.Value,
                target.UnitId is null, primary.Tactic.Id, [primary.Officer.Id], false, intelligenceEffect));
            IntelligenceTactics.Resolve(world, unit.Id, primary.Officer, primary.Tactic, target, intelligenceEffect, rules, events);
            var learning = primary.Officer.EquippedTacticId is int selected && military.Tactics[selected].Effect == TacticEffect.TrapSelection
                ? military.Tactics[selected] : primary.Tactic;
            Grow(world, members, [primary.Officer.Id], learning, events);
            return;
        }
        List<Participant> participants = [primary];
        foreach (var other in candidates.Where(p => p.Officer.Id != primary.Officer.Id && p.Tactic.Effect == TacticEffect.Martial && p.Tactic.Category == primary.Tactic.Category))
        {
            int distance = Math.Abs(primary.Officer.Affinity - other.Officer.Affinity);
            distance = Math.Min(50, Math.Min(distance, 150 - distance));
            int linkChance = (25 + Math.Max(0, 5 * (5 - distance))) * profile.LinkMultiplier;
            if (Roll(world, "military-tactic-link", linkChance)) participants.Add(other);
        }
        int category = primary.Tactic.Category!.Value;
        int proficiency = primary.Officer.TacticProficiencies[category];
        var defenders = world.Officers.Values.Where(p => p.IsServing && (target.UnitId is int enemy ? p.UnitId == enemy :
            p.SettlementId == target.SettlementId && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null)));
        int resistance = Math.Max(0, (defenders.Select(p => p.TacticProficiencies[category]).DefaultIfEmpty(0).Max() - proficiency) / 20);
        int effect = Roll(world, "military-tactic-resistance", resistance) ? 1 : 10;
        bool doubled = Roll(world, "military-tactic-double", 1 + participants[^1].Officer.TacticProficiencies[category] / 100);
        foreach (var participant in participants)
            world.Replace(world.Officers[participant.Officer.Id] with
            { TacticCooldown = participant.Officer.Id == primary.Officer.Id ? primary.Tactic.MinimumInterval : participant.Tactic.MinimumInterval / 3 });
        events.Add(new TacticActivatedEvent(world.Clock, unit.Id, false, target.UnitId ?? target.SettlementId!.Value,
            target.UnitId is null, primary.Tactic.Id, participants.Select(p => p.Officer.Id).ToImmutableArray(), doubled, effect));
        var damageMembers = participants.Select(p => new MartialTacticMember(p.Tactic.Power,
            p.Officer.TacticProficiencies[category], world.Abilities(p.Officer).War, p.Officer.Id != primary.Officer.Id)).ToArray();
        if (target.UnitId is int targetId)
        {
            var defender = world.Units[targetId];
            var defense = military.Formations[defender.FormationId!.Value].Combat!;
            uint ordinary = CombatMath.TroopDamage(MilitaryCombat.Strength(world, unit), MilitaryCombat.Strength(world, defender),
                formation.Combat!, defense, false, 0);
            uint damage = TacticMath.MartialDamage(damageMembers, doubled, ordinary, effect,
                defender.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed, defender.Soldiers);
            MilitaryCombat.DamageUnit(world, defender, damage, defense, unit.Id, events);
            var current = world.Units[targetId];
            if (current.Soldiers == 0 || current.Morale == 0) BattleAftermath.TerminateUnit(world, targetId, unit.Id, rules, events);
        }
        else
        {
            var defender = world.Settlements[target.SettlementId!.Value];
            var defense = military.Place(defender).Combat!;
            uint ordinary = CombatMath.TroopDamage(MilitaryCombat.Strength(world, unit), MilitaryCombat.GarrisonStrength(world, defender),
                formation.Combat!, defense, true, defender.Durability);
            uint damage = TacticMath.MartialDamage(damageMembers, doubled, ordinary, effect, false, defender.Soldiers);
            MilitaryCombat.DamageSettlement(world, defender, damage, defense, unit.Id, events);
            var current = world.Settlements[defender.Id];
            if (MilitaryCombat.Defeated(current)) BattleAftermath.ResolveSettlementDefeat(world, defender.Id, unit.Id, rules, events);
        }
        Grow(world, members, participants.Select(p => p.Officer.Id).ToArray(), primary.Tactic, events);
    }

    private static TacticRule? ChooseTactic(WorldMutation world, OfficerState officer, int owner, FormationRule formation,
        MilitaryOrder target, SimulationRules rules)
    {
        var military = rules.Military!;
        var ids = formation.Tactics!.AutomaticTactics.IsEmpty ? officer.EquippedTacticId is int selected ? new[] { selected } : [] :
            formation.Tactics.AutomaticTactics.ToArray();
        var available = ids.Where(id => !formation.Tactics.AutomaticTactics.IsEmpty ||
            MilitaryTactics.HasTactic(world, owner, [officer.Id], id, rules.Treasures)).Select(id => military.Tactics[id])
            .Where(t => t.CompatibleFormations.Contains(formation.Id) && (target.UnitId is not null || t.AffectsFacilities));
        var position = target.UnitId is int unitId ? world.Units[unitId].Position : military.Place(world.Settlements[target.SettlementId!.Value]).Position;
        return available.Select(t => t.Effect == TacticEffect.TrapSelection ? military.TrapAt(position) : t)
            .OfType<TacticRule>().OrderByDescending(t => t.Power).ThenBy(t => t.Id).FirstOrDefault(t => (t.Effect == TacticEffect.Martial || IntelligenceTactics.IsOffensive(t.Effect)) &&
            t.CompatibleFormations.Contains(formation.Id) && (target.UnitId is not null || t.AffectsFacilities) &&
            t.Category is not null);
    }

    internal static int AllocationChance(BattleRank rank, int members, bool favored)
    {
        int count = members switch { 1 => 20, 2 => 14, 3 => 10, 4 => 9, >= 5 => 8, _ => throw new ArgumentOutOfRangeException(nameof(members)) };
        int position = rank switch { BattleRank.Front => 5, BattleRank.Middle => 4, BattleRank.Rear => 3, _ => throw new ArgumentException("Unknown battle rank.") };
        return position * count * (favored ? 2 : 1) * 5 / 8;
    }

    internal static bool Roll(WorldMutation world, string stream, int chance) =>
        chance > 0 && (chance >= 100 || world.NextRandom(stream) % 100 < (uint)chance);

    internal static void Grow(WorldMutation world, OfficerState[] members, IReadOnlyList<int> participants, TacticRule tactic, List<DomainEvent> events)
    {
        int category = tactic.Category!.Value;
        foreach (var original in members.OrderBy(p => p.Id))
        {
            var officer = world.Officers[original.Id];
            int before = officer.TacticProficiencies[category];
            int next = before >= 1000 ? before : Math.Min(1000, before + (officer.MasteredCategories.Contains(category) ? 9 : 3));
            officer = officer with { TacticProficiencies = officer.TacticProficiencies.SetItem(category, next) };
            if (next != before) events.Add(new TacticProficiencyGrownEvent(world.Clock, officer.Id, category, before, next));
            if (tactic.Selectable && !officer.LearnedTactics.Contains(tactic.Id) && next >= tactic.TrainingRequirement &&
                Roll(world, "military-tactic-learning", Math.Min(70, (next - tactic.TrainingRequirement) / 5 + 30)))
            {
                officer = officer with { LearnedTactics = officer.LearnedTactics.Add(tactic.Id) };
                events.Add(new TacticLearnedEvent(world.Clock, officer.Id, tactic.Id));
            }
            world.Replace(officer);
        }
        var actor = world.Officers[participants[(int)(world.NextRandom("military-tactic-growth-member") % (uint)participants.Count)]];
        bool martial = tactic.Effect == TacticEffect.Martial;
        int ability = martial ? actor.BaseAbilities.War : actor.BaseAbilities.Intelligence;
        if (ability < 100 && Roll(world, "military-tactic-growth", 3))
        {
            world.Replace(actor with { BaseAbilities = martial ? actor.BaseAbilities with { War = ability + 1 } : actor.BaseAbilities with { Intelligence = ability + 1 } });
            events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, martial ? "war" : "intelligence", ability, ability + 1));
        }
    }
}
