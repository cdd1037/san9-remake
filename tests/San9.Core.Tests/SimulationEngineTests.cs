using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SimulationEngineTests
{
    [Fact]
    public void AdvanceXun_IsDeterministicForSameSeedAndCommands()
    {
        SimulationEngine first = CreateEngine();
        SimulationEngine second = CreateEngine();
        WaitCommand[] commands =
        [
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"), new ForceId(2), 0),
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), new ForceId(1), 0),
        ];

        foreach (WaitCommand command in commands)
        {
            Assert.True(first.Submit(command).Accepted);
        }

        foreach (WaitCommand command in commands.Reverse())
        {
            Assert.True(second.Submit(command).Accepted);
        }

        AdvanceXunResult firstResult = first.AdvanceXun();
        AdvanceXunResult secondResult = second.AdvanceXun();

        Assert.Equal(firstResult.Snapshot.StateHash, secondResult.Snapshot.StateHash);
        Assert.Equal(firstResult.Events, secondResult.Events);
        CommandResolvedEvent[] resolved = firstResult.Events.OfType<CommandResolvedEvent>().ToArray();
        Assert.Equal(new ForceId(1), resolved[0].IssuerId);
        Assert.Equal(new ForceId(2), resolved[1].IssuerId);
    }

    [Fact]
    public void Submit_RejectsDuplicateCommandId()
    {
        SimulationEngine engine = CreateEngine();
        Guid id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        WaitCommand command = new(id, new ForceId(1), 0);

        Assert.True(engine.Submit(command).Accepted);
        CommandSubmissionResult duplicate = engine.Submit(command);

        Assert.False(duplicate.Accepted);
        Assert.Equal(CommandRejection.DuplicateCommandId, duplicate.Rejection);
    }

    [Fact]
    public void Validate_DoesNotReserveCommandIdOrQueueCommand()
    {
        SimulationEngine engine = CreateEngine();
        WaitCommand command = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new ForceId(1),
            0);

        Assert.True(engine.Validate(command).Accepted);
        Assert.True(engine.Validate(command).Accepted);
        Assert.True(engine.Submit(command).Accepted);

        AdvanceXunResult result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<CommandResolvedEvent>());
    }

    private static SimulationEngine CreateEngine()
    {
        WorldState world = WorldState.Create(
            new GameClock(190, 1, XunIndex.Early),
            seed: 190UL);
        return new SimulationEngine(world);
    }
}
