namespace San9.Core.Simulation;

public sealed class MissingTreasureDefinitionException(int treasureId)
    : ArgumentException($"Missing definition for treasure ID {treasureId}.")
{
    public int TreasureId { get; } = treasureId;
}
