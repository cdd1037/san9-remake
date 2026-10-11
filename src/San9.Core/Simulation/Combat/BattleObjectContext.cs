using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Identity and geography at the occurrence, retained after the combat object is removed.</summary>
public sealed record BattleObjectContext(int Id, bool IsSettlement, int? ForceId, int? CommanderId,
    WorldPosition? Position, int? ServicePlaceId, bool IsGarrison)
{
    internal static BattleObjectContext Capture(WorldMutation world, BattleSource source)
    {
        if (!source.IsSettlement) return Capture(world.Units[source.Id]);
        var place = world.Settlements[source.Id];
        return new(place.Id, true, place.OwnerId,
            world.Officers.Values.FirstOrDefault(p => p.SettlementId == place.Id && p.IsCommander)?.Id,
            null, place.Id, true);
    }

    internal static BattleObjectContext Capture(UnitState unit) => new(unit.Id, false, unit.OwnerId,
        unit.CommanderId, unit.Position, unit.ServicePlaceId, unit.IsEmbedded);
}
