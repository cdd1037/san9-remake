using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Shared geographic inputs; derived coverage is never authored per cell.</summary>
public sealed record BattlefieldGeography(int FootprintScale, ImmutableArray<MapCell> BlockedCells)
{
    public static BattlefieldGeography Empty { get; } = new(1, []);
}
public sealed record CityInfluenceRule(int RadiusCells, int LandTacticBonus);
public sealed record RockfallRule(int PropagationSteps, int AttackRange);
