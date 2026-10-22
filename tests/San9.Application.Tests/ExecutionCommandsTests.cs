using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ExecutionCommandsTests
{
    [Theory]
    [InlineData(0, ControllerKind.Automatic)]
    [InlineData(1, ControllerKind.Automatic)]
    [InlineData(2, ControllerKind.Automatic)]
    [InlineData(0, ControllerKind.Player)]
    [InlineData(1, ControllerKind.Player)]
    [InlineData(2, ControllerKind.Player)]
    public void ExecutionRowsUseEachPlayerTasksOwnerRatherThanTheActiveSeat(int activeControl, ControllerKind secondKind)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var started = world.Clock;
        var due = started;
        for (int i = 0; i < 9; i++) due = due.NextDay();
        var date = started.NextDay().NextDay().NextDay();
        var own = new PersistentTask(Guid.NewGuid(), "diplomacy-prisoner-return", "player", 1, 1, 1,
            started, due, 3, 0)
        {
            Journey = new(2, null, null) { Route = new(new(0, 0), new(300, 0), false) },
            PrisonerReturn = new(2, 5)
        };
        var other = own with { Id = Guid.NewGuid(), Kind = "personnel-search", ControllerId = "second", ForceId = 2,
            OfficerId = 4, OriginSettlementId = 2, Sequence = 1, PrisonerReturn = null,
            Journey = own.Journey with { DestinationPlaceId = 1 } };
        var view = new StrategySnapshot(definition, world with
        {
            Clock = date,
            Simulation = world.Simulation with
            {
                Controls = [new("player", 1, ControllerKind.Player), new("second", 2, secondKind)],
                ActiveControl = activeControl, Tasks = [own, other]
            }
        });
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .ToDictionary(key => key, key => key));

        var rows = ExecutionCommands.Read(view, texts);
        Assert.Equal(secondKind == ControllerKind.Player ? 2 : 1, rows.Count);
        Assert.Equal((1, "交还", "officer.4", 7L, true),
            (rows[0].OfficerId, rows[0].Command, rows[0].Target, rows[0].RemainingDays, rows[0].FinishesThisXun));
        if (secondKind == ControllerKind.Player)
            Assert.Equal((4, "探索", "city.1", 7L), (rows[1].OfficerId, rows[1].Command, rows[1].Target, rows[1].RemainingDays));
        var catalog = Assert.Single(OfficerCatalog.Read(view, texts, 2, null, null, 1).Rows);
        Assert.Equal(view.PlayerForceId == 1 ? "交还" : "＊＊", catalog.Cells[1].Text);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(11, 10)]
    [InlineData(21, 10)]
    [InlineData(21, 20)]
    public void JourneyCountdownMarksOnlyTheFinishingXun(int startDay, int duration)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var started = new GameClock(190, 12, startDay);
        var date = started;
        var due = started;
        for (int i = 0; i < duration - 1; i++) due = due.NextDay();
        var task = new PersistentTask(Guid.NewGuid(), "personnel-move", "player", 1, 1, 1,
            started, due, 0, 0)
        { Journey = new(2, null, null) { Route = new(new(0, 0), new(duration * 30, 0), false) } };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .ToDictionary(key => key, key => key));
        for (int elapsed = 0; elapsed < duration; elapsed++)
        {
            var snapshot = world with
            {
                Clock = date,
                Simulation = world.Simulation with { Tasks = [task with { ProgressDays = elapsed }] }
            };
            var row = Assert.Single(ExecutionCommands.Read(new(definition, snapshot), texts));
            Assert.Equal(duration - elapsed, row.RemainingDays);
            Assert.Equal(elapsed >= duration - 10, row.FinishesThisXun);
            date = date.NextDay();
        }
        var finished = world with { Clock = date, Simulation = world.Simulation with { Tasks = [] } };
        Assert.Empty(ExecutionCommands.Read(new(definition, finished), texts));
    }

}
