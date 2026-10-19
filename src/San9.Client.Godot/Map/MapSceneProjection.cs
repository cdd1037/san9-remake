using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed record MapSceneProjection(BuildingVisualState[] BuildingStates,
    MapBuilding[] Buildings, MapAnimatedSprite[] Animations, Dictionary<int, UnitVisualState> Units)
{
    public static MapSceneProjection Create(StrategySnapshot state, MapPackage map, BattleMapFrame? frame = null)
    {
        var forces = state.Forces.ToDictionary(f => f.Id);
        var buildingStates = (frame?.Settlements.Values ?? state.Settlements).Select(s =>
        {
            var force = s.OwnerId is int owner ? forces[owner] : null;
            return new BuildingVisualState(s.Id, s.Durability, s.Appearance, Math.Max(s.Commerce, s.Cultivation),
                s.Soldiers, force?.ColorCode ?? 50, force?.FlagGlyph ?? -1)
            {
                IsDestroyed = s.IsDestroyed, Placement = s.FacilityTypeId is int type ? new(s.Id, s.X, s.Y, type) : null,
                IsEngaged = state.IsSettlementEngaged(s.Id), IsUnderConstruction = s.IsUnderConstruction,
                Morale = s.Morale, Month = state.Month
            };
        }).ToArray();
        var visuals = BuildingVisuals.Resolve(map, buildingStates);
        var unitPositions = state.World.Units.ToDictionary(u => u.Id, u => u.Position);
        var units = state.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.FormationId is not null && u.Soldiers > 0 &&
                frame?.RemovedUnits.Contains(u.Id) != true)
            .Select(u =>
            {
                var order = u.EffectiveOrder;
                WorldPosition? target = u.EngagementTargetId is null && u.Waypoint is { } via ? via :
                    order?.UnitId is int targetId && unitPositions.TryGetValue(targetId, out var position) ? position :
                    order?.Position ?? (order?.SettlementId is int place ?
                        state.Settlements.FirstOrDefault(s => s.Id == place) is {} destination ? MapCoordinates.Center(new(destination.X, destination.Y)) : null : null);
                var commander = u.CommanderId is int commanderId ? state.Officers.FirstOrDefault(o => o.Id == commanderId) : null;
                var force = forces[u.OwnerId];
                return new UnitVisualState(u.Id,
                    (u.Position.X - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    (u.Position.Y - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell,
                    map.Squares[u.Cell.Y * map.Width + u.Cell.X].Elevation, u.Soldiers, state.FormationId(u)!.Value,
                    u.Condition, UnitVisuals.Direction(u.Position, target),
                    u.EffectiveOrderClass == UnitOrderClass.Military && state.HasCityInfluence(u.OwnerId, u.Position),
                    force.ColorCode, commander?.FlagGlyph ?? -1)
                {
                    IsEngaged = state.IsUnitEngaged(u.Id),
                    VisionAngleDegrees = state.Information.Formations.Single(f => f.Id == state.FormationId(u)).Presentation?.VisionAngleDegrees ?? 0,
                    Morale = u.Morale, CommanderNameKey = commander?.NameKey,
                    IsRulerCommander = u.CommanderId is int leader && force.LeaderId == leader,
                    UsesImperialAppearance = force.UsesImperialAppearance
                };
            }).ToDictionary(u => u.Id);
        return new(buildingStates, visuals.Buildings, visuals.Animations, units);
    }
}
