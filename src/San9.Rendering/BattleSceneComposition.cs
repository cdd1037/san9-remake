using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Maps;

namespace San9.Rendering;

public static class BattleSceneComposition
{
    public static ImmutableArray<BattleSceneLayer> Compose(BattleSceneDefinition definition, MapSquare source, MapSquare target,
        string season, int? sourceFacility, int? targetFacility, bool touching)
    {
        BattleTerrainAppearance Terrain(MapSquare square) => definition.Terrains.First(t => t.Terrains.Contains(square.Terrain) &&
            t.Season == season && square.Elevation >= t.MinimumElevation && square.Elevation <= t.MaximumElevation &&
            (t.TextureVariants is null || t.TextureVariants.Contains(square.TextureVariant)));
        var left = Terrain(source); var right = Terrain(target);
        var sourceBuilding = sourceFacility is int sourceId ? definition.Facilities.GetValueOrDefault(sourceId) : null;
        var targetBuilding = targetFacility is int targetId ? definition.Facilities.GetValueOrDefault(targetId) : null;
        List<BattleSceneLayer> layers = [];
        if (targetBuilding?.ReplacesTerrain != true) layers.Add(right.Target);
        else layers.Add(targetBuilding.Target[0]);
        if (sourceBuilding?.ReplacesTerrain != true) layers.Add(left.Source);
        if (touching)
        {
            if (definition.Transitions.FirstOrDefault(t => t.SourceKind == left.TransitionKind && t.TargetKind == right.TransitionKind) is { } transition)
                layers.Add(transition.Layer);
        }
        else
        {
            if (right.TargetEdge is { } targetEdge) layers.Add(targetEdge);
            if (left.SourceEdge is { } sourceEdge) layers.Add(sourceEdge);
        }
        if (targetBuilding is not null) layers.AddRange(targetBuilding.ReplacesTerrain ? targetBuilding.Target.Skip(1) : targetBuilding.Target);
        if (sourceBuilding is not null) layers.AddRange(sourceBuilding.Source);
        return layers.ToImmutableArray();
    }
}
