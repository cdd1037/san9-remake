using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Applies equipped officer effects independently of the mixed-troop module.</summary>
internal static class EquipmentCombat
{
    internal static uint Adjust(WorldMutation world, BattleSource source, BattleSource target, uint damage,
        int? actorId = null, bool tactic = false, bool ranged = false)
    {
        if (damage == 0) return 0;
        int attack = source.IsSettlement || !world.Units.TryGetValue(source.Id, out var attacking) ? 0 :
            Bonus(world, attacking, actorId, e => e.AttackPercent + (tactic ? e.MartialTacticPercent : 0));
        int defense = target.IsSettlement || !world.Units.TryGetValue(target.Id, out var defending) ? 0 :
            Bonus(world, defending, null, e => e.DefensePercent + (ranged ? e.RangedDefensePercent : 0));
        ulong adjusted = (ulong)damage * (uint)(10000 + attack) / 10000 * (uint)(10000 - defense) / 10000;
        return (uint)Math.Min(adjusted, uint.MaxValue);
    }

    private static int Bonus(WorldMutation world, UnitState unit, int? actorId, Func<EquipmentCombatEffects, int> select)
    {
        int Officer(int id) => Math.Min(30, world.EquipmentCombatEffects(id).Sum(select));
        if (actorId is int actor) return Officer(actor) * 100;
        var members = world.UnitMembers(unit.Id).Where(person => person.IsServing).OrderBy(person => person.Id).ToArray();
        if (members.Length == 0) return 0;
        if (!unit.Detachments.IsEmpty)
        {
            ulong total = 0, strength = 0;
            foreach (var member in members)
            {
                uint soldiers = unit.Detachments.GetValueOrDefault(member.Id)?.Soldiers ?? 0;
                total += (ulong)Officer(member.Id) * 100 * soldiers;
                strength += soldiers;
            }
            return strength == 0 ? 0 : (int)(total / strength);
        }
        return members.Sum(member => Officer(member.Id)) * 100 / members.Length;
    }
}
