using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class MixedTroopCombat
{
    internal static uint Adjust(WorldMutation world, BattleSource source, BattleSource target,
        uint damage, MilitaryRulebook rules, int? actorId = null, bool tactic = true, bool rangedSource = false)
    {
        uint adjusted = AdjustBase(world, source, target, damage, rules, actorId, tactic, rangedSource);
        if (!world.Simulation.NewGame.MixedTroopsEnabled || adjusted == 0) return adjusted;
        int attack = !source.IsSettlement && world.Units.TryGetValue(source.Id, out var attacker) &&
            !rules.Map.IsWater(attacker.Position) ? ArmamentBonus(attacker, rules, ArmamentSlot.Weapon, actorId) : 0;
        int defense = !target.IsSettlement && world.Units.TryGetValue(target.Id, out var defender) &&
            !rules.Map.IsWater(defender.Position) ? ArmamentBonus(defender, rules, ArmamentSlot.Armor, null) : 0;
        ulong result = (ulong)adjusted * (uint)(10000 + attack) / 10000 * (uint)(10000 - defense) / 10000;
        return (uint)Math.Min(result, uint.MaxValue);
    }

    internal static int ArmamentBonus(UnitState unit, MilitaryRulebook rules, ArmamentSlot slot, int? actorId)
    {
        int Bonus(DetachmentState detachment)
        {
            var allocation = slot == ArmamentSlot.Weapon ? detachment.Weapon : detachment.Armor;
            if (allocation is null || !rules.Armaments.TryGetValue(allocation.DefinitionId, out var definition) ||
                definition.Slot != slot || !definition.Supports(detachment.TroopTypeId)) return 0;
            return allocation.EffectiveBonusBasisPoints(slot == ArmamentSlot.Weapon ? definition.AttackBonus : definition.DefenseBonus,
                detachment.Soldiers);
        }
        if (actorId is int id) return unit.Detachments.TryGetValue(id, out var actor) ? Bonus(actor) : 0;
        if (unit.Soldiers == 0) return 0;
        ulong weighted = 0;
        foreach (var detachment in unit.Detachments.Values)
            weighted += (ulong)Bonus(detachment) * detachment.Soldiers;
        return (int)Math.Min(weighted / unit.Soldiers, 10000UL);
    }

    private static uint AdjustBase(WorldMutation world, BattleSource source, BattleSource target,
        uint damage, MilitaryRulebook rules, int? actorId, bool tactic, bool rangedSource)
    {
        if (damage == 0) return damage;
        var profile = rules.MixedTroopCombat;
        if (source.IsSettlement)
        {
            if (profile is null || !rangedSource || target.IsSettlement || !world.Units.TryGetValue(target.Id, out var unit) ||
                unit.Detachments.IsEmpty || rules.Map.IsWater(unit.Position) ||
                unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
                !rules.Formation(unit).SupportsShieldCover) return damage;
            int cover = FrontShare(unit, rules, type => type.SupportsShieldCover);
            return (uint)((ulong)damage * (10000u - (uint)((long)profile.ShieldCoverPercent * cover / 100)) / 10000);
        }
        if (
            !world.Units.TryGetValue(source.Id, out var attacker) || attacker.Detachments.IsEmpty ||
            rules.Map.IsWater(attacker.Position)) return damage;

        var attackingFormation = rules.Formation(attacker);
        int ranged = Share(attacker, rules, type => type.IsRangedAttack, actorId);
        int rangedInfantry = Share(attacker, rules, type => type.IsRangedInfantry, actorId);
        ulong adjusted = damage;
        if (actorId is int participant && attacker.FormationId is int activeFormation &&
            world.Officers.TryGetValue(participant, out var officer) &&
            officer.FormationProficiencies?.TryGetValue(activeFormation, out int experience) == true)
            adjusted = adjusted * (10000u + (uint)experience) / 10000;
        if (profile is null) return (uint)Math.Min(adjusted, uint.MaxValue);
        if (tactic && actorId is not null && attackingFormation.SupportsSpearBowSynergy &&
            (attacker.Condition is UnitCondition.Normal or UnitCondition.HighSpirits) && rangedInfantry > 0 &&
            attacker.MemberPositions.TryGetValue(actorId.Value, out var actorPosition) && actorPosition.Rank == BattleRank.Rear)
        {
            int supporting = FrontShare(attacker, rules, type => type.SupportsSpearDefense);
            adjusted = adjusted * (10000u + (uint)((long)profile.SpearBowPercent * supporting / 100)) / 10000;
        }
        if (target.IsSettlement || !world.Units.TryGetValue(target.Id, out var defender) || defender.Detachments.IsEmpty ||
            rules.Map.IsWater(defender.Position)) return (uint)Math.Min(adjusted, uint.MaxValue);

        bool ready = defender.Condition is UnitCondition.Normal or UnitCondition.HighSpirits;
        var defendingFormation = rules.Formation(defender);
        int mounted = Share(attacker, rules, type => type.IsMountedMelee, actorId);
        int spear = ready && defendingFormation.SupportsSpearDefense
            ? FrontShare(defender, rules, type => type.SupportsSpearDefense) : 0;
        int shield = ready && defendingFormation.SupportsShieldCover
            ? FrontShare(defender, rules, type => type.SupportsShieldCover) : 0;
        adjusted = adjusted * (10000u - (uint)((long)profile.SpearDefensePercent * spear * mounted / 1000000)) / 10000;
        adjusted = adjusted * (10000u - (uint)((long)profile.ShieldCoverPercent * shield * ranged / 1000000)) / 10000;
        bool frontLine = ready && defender.Detachments.Any(pair => pair.Value.Soldiers > 0 &&
            defender.MemberPositions.TryGetValue(pair.Key, out var position) && position.Rank == BattleRank.Front);
        if (!frontLine)
        {
            int vulnerable = Share(defender, rules, type => type.IsRangedInfantry);
            adjusted = adjusted * (10000u + (uint)((long)profile.MountedAttackPercent * vulnerable * mounted / 1000000)) / 10000;
        }
        return (uint)Math.Min(adjusted, uint.MaxValue);
    }

    private static int Share(UnitState unit, MilitaryRulebook rules, Func<TroopTypeDefinition, bool> matches, int? actorId = null)
    {
        if (actorId is int actor)
            return unit.Detachments.TryGetValue(actor, out var individual) && individual.Soldiers > 0 &&
                rules.TroopTypes.TryGetValue(individual.TroopTypeId, out var kind) && matches(kind) ? 10000 : 0;
        ulong amount = 0;
        foreach (var detachment in unit.Detachments.Values)
            if (rules.TroopTypes.TryGetValue(detachment.TroopTypeId, out var type) && matches(type)) amount += detachment.Soldiers;
        return unit.Soldiers == 0 ? 0 : (int)(amount * 10000 / unit.Soldiers);
    }

    private static int FrontShare(UnitState unit, MilitaryRulebook rules, Func<TroopTypeDefinition, bool> matches)
    {
        ulong amount = 0;
        foreach (var (officer, detachment) in unit.Detachments)
            if (unit.MemberPositions.TryGetValue(officer, out var position) && position.Rank == BattleRank.Front &&
                rules.TroopTypes.TryGetValue(detachment.TroopTypeId, out var type) && matches(type)) amount += detachment.Soldiers;
        return unit.Soldiers == 0 ? 0 : (int)(amount * 10000 / unit.Soldiers);
    }
}
