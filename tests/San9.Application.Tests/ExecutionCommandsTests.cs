using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ExecutionCommandsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(21)]
    public void TenDayJourneyShowsCountdownThroughoutItsExecutionXun(int startDay)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var started = new GameClock(190, 12, startDay);
        var date = started;
        var due = started;
        for (int i = 0; i < 9; i++) due = due.NextDay();
        var task = new PersistentTask(Guid.NewGuid(), "personnel-move", "player", 1, 1, 1,
            started, due, 0, 0)
        { Journey = new(2, null, null) { Route = new(new(0, 0), new(300, 0), false) } };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .ToDictionary(key => key, key => key));
        for (int elapsed = 0; elapsed < 10; elapsed++)
        {
            var snapshot = world with
            {
                Clock = date,
                Simulation = world.Simulation with { Tasks = [task with { ProgressDays = elapsed }] }
            };
            var row = Assert.Single(ExecutionCommands.Read(new(definition, snapshot), texts));
            Assert.Equal(10 - elapsed, row.RemainingDays);
            Assert.True(row.FinishesThisXun);
            date = date.NextDay();
        }
        var finished = world with { Clock = date, Simulation = world.Simulation with { Tasks = [] } };
        Assert.Empty(ExecutionCommands.Read(new(definition, finished), texts));
    }

    [Fact]
    public void JourneyCrossingXunShowsCountdownOnlyInItsFinishingXun()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var started = new GameClock(190, 12, 21);
        var date = started;
        var due = started;
        for (int i = 0; i < 19; i++) due = due.NextDay();
        var task = new PersistentTask(Guid.NewGuid(), "personnel-move", "player", 1, 1, 1,
            started, due, 0, 0)
        { Journey = new(2, null, null) { Route = new(new(0, 0), new(600, 0), false) } };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .ToDictionary(key => key, key => key));
        for (int elapsed = 0; elapsed < 20; elapsed++)
        {
            var snapshot = world with
            {
                Clock = date,
                Simulation = world.Simulation with { Tasks = [task with { ProgressDays = elapsed }] }
            };
            var row = Assert.Single(ExecutionCommands.Read(new(definition, snapshot), texts));
            Assert.Equal(20 - elapsed, row.RemainingDays);
            Assert.Equal(elapsed >= 10, row.FinishesThisXun);
            date = date.NextDay();
        }
    }
}
