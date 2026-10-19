using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class DomesticOriginFilteringTests
{
    private static (SimulationEngine Engine, OriginalAiProjection Projection) Setup(int state)
    {
        var basis = MissionScenario.Create();
        var combat = new CombatProfile(10, 10, 10, 10, 50);
        var content = basis with { Military = basis.Military! with
        {
            Formations = [basis.Military.Formations[0] with { Combat = combat, Attack = new(10, true), CaptureRanks = [.. Enumerable.Repeat(BattleRank.Middle, basis.Military.Formations[0].OfficerCapacity)] }],
            Places = [.. basis.Military.Places.Select(p => p with { Combat = combat })]
        } };
        var world = GameSession.Start(basis, 1).Snapshot;
        world = world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with
                { Soldiers = 5000, DraftPopulation = 10000, Morale = 90, ConfusionDaysRemaining = state == 1 ? 5 : 0 } : s).ToArray(),
            Officers = world.Officers.Select(p => p.OwnerId == 1 ? p with { BaseAbilities = new(100, 100, 100, 100) } : p).ToArray(),
            Corps = world.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(100000, 1000000) } : c).ToArray(),
            Units = [.. world.Units.Select(u => u.Id == 20 ? u with
                { MemberRanks = u.MemberRanks.SetItem(23, BattleRank.Middle), Position = state == 2 ? new(4, 7) : new(42, 7), Order = new(MilitaryOrderKind.AttackSettlement, null, 1) }
                : u with { MemberRanks = u.MemberRanks.SetItem(3, BattleRank.Middle), Position = new(102, 7), Order = null })]
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(world));
        var military = new MilitaryRulebook(content.Military!);
        Assert.Equal(state == 2, MilitaryEngagementQuery.Capture(engine.Snapshot, military).Settlements.Contains(1));
        return (engine, new(engine, content, 1, new(content, military, engine.Snapshot), true));
    }

    [Theory]
    [InlineData(DomesticActivity.Recruit, 0)]
    [InlineData(DomesticActivity.Recruit, 1)]
    [InlineData(DomesticActivity.Recruit, 2)]
    [InlineData(DomesticActivity.Train, 0)]
    [InlineData(DomesticActivity.Train, 1)]
    [InlineData(DomesticActivity.Train, 2)]
    public void ProductionProjectionSkipsBlockedOriginsBeforeDrawingOrSubmitting(DomesticActivity activity, int state)
    {
        var (engine, projection) = Setup(state);
        var before = engine.Snapshot;
        int draws = 0, attempts = 0, rejected = 0;
        var result = OriginalDomesticWork.Run(activity, 10, projection.Domestic, () => { draws++; return 0; }, development =>
        {
            attempts++;
            var submission = engine.Execute(new("player"), new DevelopCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, development)).Submission;
            if (!submission.Accepted) rejected++;
            return submission.Accepted;
        });
        Assert.Equal(state == 0 ? 1 : 0, attempts);
        Assert.Equal(0, rejected);
        Assert.Equal(state == 0 ? 1 : 0, result.Steps.Length);
        if (state != 0)
        {
            Assert.Equal(0, draws);
            Assert.Empty(projection.Domestic().Sites);
            Assert.Null(projection.Observe(1));
            StateAssert.Equal(before, engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BlockedOriginsDoNotConsumeTradeOrResidualAttempts(int state)
    {
        var (engine, projection) = Setup(state);
        var before = engine.Snapshot;
        int calls = 0;
        var trade = OriginalAiEconomy.Trade(1, 10, projection.Domestic, () => engine.Snapshot,
            (id, people, direction) => { calls++; return engine.GetFoodTradeQuote(1, id, people, direction); },
            (_, _, _) => { calls++; return false; });
        var residual = OriginalAiEconomy.Residual(1, false, 10, projection.Domestic, () => engine.Snapshot,
            _ => { calls++; return false; }, (_, _) => { calls++; return false; }, projection.Observe);
        Assert.Equal(0, calls);
        Assert.Equal(0, trade.Attempts);
        Assert.Equal(0, residual.Attempts);
        StateAssert.Equal(before, engine.Snapshot);
    }
}
