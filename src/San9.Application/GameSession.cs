using San9.Core.Simulation;

namespace San9.Application;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation;

    public GameSession(WorldState initialState)
    {
        _simulation = new SimulationEngine(initialState);
    }

    public WorldSnapshot Snapshot => _simulation.Snapshot;

    public CommandSubmissionResult Validate(GameCommand command) => _simulation.Validate(command);

    public CommandSubmissionResult Submit(GameCommand command) => _simulation.Submit(command);

    public AdvanceXunResult AdvanceXun() => _simulation.AdvanceXun();
}
