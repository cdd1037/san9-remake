namespace San9.Content.Interface;

public sealed record BattleSceneLayer(string Asset, int X, int Y);
public sealed record BattleTerrainAppearance(int[] Terrains, string Season, int MinimumElevation, int MaximumElevation,
    int[]? TextureVariants, string TransitionKind, BattleSceneLayer Source, BattleSceneLayer Target,
    BattleSceneLayer? SourceEdge, BattleSceneLayer? TargetEdge);
public sealed record BattleTerrainTransition(string SourceKind, string TargetKind, BattleSceneLayer Layer);
public sealed record BattleFacilityBackdrop(BattleSceneLayer[] Source, BattleSceneLayer[] Target, bool ReplacesTerrain);
public sealed record BattleSceneDefinition(BattleTerrainAppearance[] Terrains, BattleTerrainTransition[] Transitions,
    Dictionary<int, BattleFacilityBackdrop> Facilities);
