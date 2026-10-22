using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class G10StageSessionTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "g10-stage", 192, 12, 1, 1,
        [new(1, "force", 1, 500) { LeaderId = 11, BaseTrust = 500 },
         new(2, "enemy", 2, 100) { LeaderId = 21, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 1000, 100, 100, 50, []) { ConsumesFood = false },
         new(2, "away", 2, 100, 0, 1000, 100, 100, 50, []) { ConsumesFood = false }],
        new() { [1] = "home", [2] = "away" }, new() { [1] = 1, [2] = 2 })
    {
        AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false,
        FreeOfficerMigrationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false, ForceConsolidationEnabled = false,
        Officers = [new("ruler", Person(11, 1, 1, OfficerStatus.Ruler)), new("successor", Person(12, 1, 1)),
            new("instigator", Person(13, 1, 1)), new("enemy", Person(21, 2, 2, OfficerStatus.Ruler)),
            new("emperor", new(99, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special })],
        ImperialCourt = new([99], []),
        ChainedPlot = new(11, 12, 13, new(192, 12, 11), 95, 1000, [13], [], 96, 80, 2, new("plot.start"), new("plot.end")),
        Terminal = new([1, 2], 193),
        SearchEvents = [new CommemorationSearchEventDefinition([new(1, null, CommunityAbility.Intelligence, [], new("memorial.1"))], 1500, 2, 2)
            { Id = "memorial", ChanceAbility = CommunityAbility.Politics, ChanceDivisor = 1, Consumption = EventConsumption.OnSuccess }]
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }], []) };
    private static OfficerState Person(int id, int force, int home, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, force, home, new(50, 50, 100, 50), false) { Status = status }.WithLoyalty(force, 100);

    [Fact]
    public void HistoricalDeathCancelsJourneyThenSuccessorCanChooseEventBeforeTerminalFreeze()
    {
        var d = Scenario() with { UnbuiltPlaces = [new(3, new(500, 0))], RegionNameKeys = new() { [1] = "home", [2] = "away", [3] = "distant" },
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1 } }; var session = GameSession.Start(d, 17);         Assert.Equal(ChainedPlotStep.Prelude, Assert.Single(session.AdvanceXun().Events.OfType<ChainedPlotEvent>()).Step);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [11], 3)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        var coup = session.AdvanceXun(); EventAssert.Equal(coup.Events, restored.AdvanceXun().Events);
        Assert.Single(coup.Events.OfType<OfficerDiedEvent>()); Assert.Equal(12, session.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 11);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [12], 1)).Submission.Accepted);
        session.AdvanceXun(); var prompt = Assert.IsType<CommunitySearchPrompt>(session.GetCommunitySearchPrompt());
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, true)
            { CommunitySearch = CommunitySearchChoice.Accept };
        var result = session.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted); Assert.Single(result.Events.OfType<CommunitySearchResolvedEvent>());
        Assert.Equal("time-limit", session.GetTerminalResult().Reason); Assert.Equal(new GameClock(193, 1, 1), session.Snapshot.Clock);
        AssertTerminalFrozenAndReplay(session, d);
    }

    [Fact]
    public void EventWithdrawalEndsLastEnemyUnitAndForceBeforeVictoryWithoutRepeatingReward()
    {
        var d = Scenario() with
        {
            ChainedPlot = null, Terminal = new([1, 2], 350),
            Settlements = Scenario().Settlements.Select(s => s.Id == 2 ? s with { X = 3 } : s).ToArray(),
            Officers = Scenario().Officers.Select(o => o.State.Id == 21 ? o with { State = o.State with { SettlementId = null, UnitId = 20 } } : o).ToArray(),
            Units = [new(20, 2, new(17, 7), 1000) { FormationId = 1, ServicePlaceId = 2, CommanderId = 21, Morale = 60 }],
            Military = new(4, 2, Enumerable.Repeat(0, 8).ToImmutableArray(), [new(0, 1, null)],
                [new(1, 1, 0, 100000, 5, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1)), new(2, new(17, 2), new(1, 1))]),
            SearchEvents = [new ScholarPunishmentSearchEventDefinition([new(1, 21, [], new("scholar.visited"))], 1)
                { Id = "scholar", ChanceAbility = ConflictChanceAbility.Politics, ChanceDivisor = 1 }]
        };
        var session = GameSession.Start(d, 17);         Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, [12], 1)).Submission.Accepted);
        session.AdvanceXun(); var prompt = Assert.IsType<SearchConflictPrompt>(session.GetSearchConflictPrompt());
        var result = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            prompt.TaskId, prompt.Step, false) { SearchConflict = SearchConflictChoice.Execute });
        Assert.True(result.Submission.Accepted); Assert.Single(result.Events.OfType<OfficerWithdrawnEvent>());
        Assert.DoesNotContain(session.Snapshot.Units, u => u.Id == 20);
        Assert.True(session.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        Assert.Equal(TerminalRoute.PlayerVictoryEnding, session.GetTerminalResult().Route);
        Assert.Single(result.Events.OfType<SearchConflictResolvedEvent>());
        AssertTerminalFrozenAndReplay(session, d);
    }
    private static void AssertTerminalFrozenAndReplay(GameSession session, ScenarioInitialState d)
    {
        var savedState = session.Capture();
        var saved = SaveService.Encode(session); var hash = session.Snapshot;
        Assert.Equal(SessionRejection.Terminal, session.Apply(session.Request(new AdvanceOperation())).Rejection);
        StateAssert.Equal(hash, session.Snapshot);
        StateAssert.Equal(savedState, SaveService.Decode(saved, d).Capture());
    }
}
