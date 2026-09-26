using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficerArrivalTests
{
    private static readonly GameClock Started = new(190, 12, 21);
    private static GameClock After(int days)
    {
        var date = Started;
        for (int i = 0; i < days; i++) date = date.NextDay();
        return date;
    }

    private static PersistentTask Task(string kind, int distance, int dueOffset, int progress = 0) =>
        new(new Guid("00000000-0000-0000-0000-000000000001"), kind, "player", 1, 1, 1,
            Started, After(dueOffset), progress, 0)
        { Journey = new(2, null, null) { Route = new(new(0, 0), new(distance, 0), false) } };

    private static (CatalogCell Cell, string Detail) Read(PersistentTask? task, int elapsed = 0,
        int? owner = 1, bool deployed = false, bool pending = false)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        world = world with
        {
            Clock = After(elapsed),
            Officers = world.Officers.Select(o => o.Id == 1 ? o with
                { OwnerId = owner, SettlementId = deployed ? null : 1, UnitId = deployed ? 10 : null } : o).ToArray(),
            Units = deployed ? [new(10, owner ?? 1, new(0, 0), 100) { FormationId = 0, CommanderId = 1 }] : [],
            Simulation = world.Simulation with
            {
                Tasks = task is null ? [] : [task],
                PendingDailyTask = pending ? new(Started, 1, []) : null
            }
        };
        var view = new StrategySnapshot(definition, world);
        var texts = new TextCatalog(4, definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
            .ToDictionary(key => key, key => new TextEntry(key, key)));
        var table = OfficerCatalog.Read(view, texts, 2, null, null, 1);
        var cell = Assert.Single(table.Rows).Cells[Array.IndexOf(table.Columns, "到达预定")];
        var detail = OfficerDetails.Read(view, texts, 1).Fields.SelectMany(f => f).Single(f => f.Label == "到达预定");
        return (cell, detail.Value);
    }

    [Theory]
    [InlineData("personnel-move", 300, 9, 0, 10)]
    [InlineData("personnel-recruit", 300, 9, 4, 6)]
    [InlineData("personnel-search", 65, 9, 0, 10)]
    [InlineData("personnel-search", 65, 9, 9, 1)]
    [InlineData("personnel-move", 0, 0, 0, 0)]
    [InlineData("personnel-move", 30, 0, 0, 1)]
    [InlineData("personnel-move", 600, 19, 12, 8)]
    public void ArrivalIsNumericCountdownNotResolutionDate(string kind, int distance, int due, int progress, int expected)
    {
        var (cell, detail) = Read(Task(kind, distance, due, progress), progress);
        Assert.Equal(new CatalogCell(expected.ToString(), expected), cell);
        Assert.Equal(cell.Text, detail);
    }

    [Theory]
    [InlineData(false, 0, 3)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 0, 3)]
    [InlineData(true, 1, 3)]
    [InlineData(true, 2, 2)]
    [InlineData(true, 5, 0)]
    public void ReturnUsesItsOwnCountdownAndCreationPhase(bool afterCommands, int progress, int expected)
    {
        var task = Task("personnel-return", 600, afterCommands ? 3 : 2, progress);
        task = task with { Journey = task.Journey! with { ReturnLeg = new(1, 3, afterCommands) } };
        Assert.Equal(expected, Read(task, progress).Cell.Number);
    }

    [Fact]
    public void DiplomacyAndStudyUseTheirOwnDurations()
    {
        var diplomatic = Task("diplomacy-surrender", 65, 10, 4) with { Surrender = new(2) };
        Assert.Equal(6, Read(diplomatic, 4).Cell.Number);
        Assert.Equal(60, Read(Task("tactic-study", 0, 90, 30), 30).Cell.Number);
    }

    [Fact]
    public void DailyDecisionAlreadyConsumedTodaysCountdown()
    {
        Assert.Equal(0, Read(Task("personnel-move", 300, 9, 9), 9, pending: true).Cell.Number);
    }

    [Theory]
    [InlineData(1, false, "—")]
    [InlineData(1, true, "—")]
    [InlineData(2, false, "**")]
    [InlineData(2, true, "**")]
    [InlineData(null, false, "**")]
    public void VisibilityPrecedesApplicability(int? owner, bool deployed, string expected)
    {
        var (cell, detail) = Read(null, owner: owner, deployed: deployed);
        Assert.Equal(expected, cell.Text);
        Assert.Equal(expected, detail);
        Assert.Null(cell.Number);
        Assert.True(cell.IsUnavailable);
    }

    [Fact]
    public void ForeignTaskDoesNotLeakDurationOrNumericSortValue()
    {
        var cell = Read(Task("personnel-search", 65, 9), owner: 2).Cell;
        Assert.Equal("**", cell.Text);
        Assert.Null(cell.Number);
    }
}
