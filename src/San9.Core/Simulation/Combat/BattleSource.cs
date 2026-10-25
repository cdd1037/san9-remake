using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>A battle participant identity, independent of a unit's or settlement's numeric identity space.</summary>
public readonly record struct BattleSource(int Id, bool IsSettlement)
{
    internal bool IsPresent(WorldMutation world) => IsSettlement
        ? world.Settlements.TryGetValue(Id, out var settlement) && !settlement.IsDestroyed
        : world.Units.ContainsKey(Id);

    // Ownership and personnel hosting qualify a carrier for combat outcomes, not for being attacked.
    internal bool IsCombatant(WorldMutation world) => IsPresent(world) && (!IsSettlement ||
        world.Settlements[Id] is { OwnerId: not null, CanHostPersons: true });
    internal int Owner(WorldMutation world) => IsSettlement ? world.Settlements[Id].OwnerId!.Value : world.Units[Id].OwnerId;
    internal int? Corps(WorldMutation world) => IsSettlement ? world.Settlements[Id].CorpsId : world.Units[Id].CorpsId;
    internal int Morale(WorldMutation world) => IsSettlement ? world.Settlements[Id].Morale : world.Units[Id].Morale;
    internal uint Soldiers(WorldMutation world) => IsSettlement ? world.Settlements[Id].Soldiers : world.Units[Id].Soldiers;
    internal uint Capacity(WorldMutation world, MilitaryRulebook rules) => IsSettlement ? world.Settlements[Id].SoldierCapacity :
        rules.Capacity(world.Units[Id]);
    internal MapOccupancy Area(IWorldQuery world, MilitaryRulebook rules) => IsSettlement ?
        rules.SettlementArea(world.Settlements[Id]) : MilitaryCombat.Area(world.Units[Id], rules);
    internal void AddSoldiers(WorldMutation world, uint soldiers)
    {
        if (IsSettlement) world.Replace(world.Settlements[Id] with { Soldiers = world.Settlements[Id].Soldiers + soldiers });
        else world.Replace(world.Units[Id] with { Soldiers = world.Units[Id].Soldiers + soldiers });
    }
    internal ResourceAccount Account => new(IsSettlement ? ResourceOwnerKind.Settlement : ResourceOwnerKind.Unit, Id);

    internal void Reward(WorldMutation world, uint wounded, int morale)
    {
        if (IsSettlement)
        {
            var settlement = world.Settlements[Id];
            world.Replace(settlement with { Wounded = settlement.Wounded + wounded, Morale = Math.Min(100, settlement.Morale + morale) });
        }
        else
        {
            var unit = world.Units[Id];
            world.Replace(unit with { Wounded = unit.Wounded + wounded, Morale = Math.Min(100, unit.Morale + morale) });
        }
    }
}
