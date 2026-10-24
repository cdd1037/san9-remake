using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OriginalAiStrategyRegressionTests
{
    private static ScenarioInitialState FourForces()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Forces = [.. basis.Forces, basis.Forces[1] with { Id = 3, LeaderId = 7, NameKey = "force.3" },
                basis.Forces[1] with { Id = 4, LeaderId = 8, NameKey = "force.4" }],
            Settlements = [.. basis.Settlements, basis.Settlements[1] with { Id = 3, OwnerId = 3, X = 3, NameKey = "city.3" },
                basis.Settlements[1] with { Id = 4, OwnerId = 4, X = 4, NameKey = "city.4" }],
            Officers = [.. basis.Officers,
                new("officer.7", new(7, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, BirthYear = 160 }),
                new("officer.8", new(8, 4, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, BirthYear = 160 })],
            Information = basis.Information with { Corps = [.. basis.Information.Corps,
                new(3, 3, 7, new(100000, 100000)) { IsRoot = true }, new(4, 4, 8, new(100000, 100000)) { IsRoot = true }] }
        };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void GlobalGroupingRanksCurrentGarrisonsBeforeSplittingOrdinaryForces(int strongest)
    {
        var content = FourForces();
        content = content with { Settlements = content.Settlements.Select(s => s with
            { Soldiers = s.OwnerId == strongest ? 90000u : 10000u }).ToArray() };
        var session = GameSession.Start(content, 190);
        Assert.Empty(session.Snapshot.Units);
        Assert.Empty(content.OriginalAi.PreferredStrategyRulerIds);

        var result = session.Apply(session.Request(new EndControlOperation(), "player"));

        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(new[] { strongest }, session.OriginalAi.Forces.Where(f => f.Group == OriginalAiGroup.Favored)
            .Select(f => f.ForceId));
        Assert.Equal(OriginalAiGroup.Unassigned, session.OriginalAi.Forces.Single(f => f.ForceId == 1).Group);
    }

    [Theory]
    [InlineData(0, OriginalAiMode.NoFrontier, 0)]
    [InlineData(40, OriginalAiMode.TargetForce, 1)]
    [InlineData(60, OriginalAiMode.TargetForce, 1)]
    public void LowerRelationForceTriggersReevaluationWithoutBecomingAnAttackCandidate(int relation,
        OriginalAiMode expected, int expectedDraws)
    {
        var basis = FourForces();
        var content = basis with
        {
            Forces = basis.Forces.Select(f => f.Id == 3 ? f with { IsBarbarian = true } : f).ToArray(),
            Information = basis.Information with { Relations = [new(1, 2, 40), new(1, 3, relation), new(1, 4, 50)] }
        };
        var session = GameSession.Start(content, 190, playerForceController: ControllerKind.Automatic);
        var world = session.Snapshot;
        var state = session.OriginalAi with { Corps = [new(1, OriginalAiMode.TargetForce, 2, null)] };
        state.Validate(world, content);
        Assert.True(OriginalAiStrategy.IsOpponent(world, 1, 2));
        Assert.False(OriginalAiStrategy.IsOpponent(world, 1, 3));
        var geography = new OriginalAiGeography(content, null, world);
        Assert.False(geography.Frontier(1, 1));
        int draws = 0;

        var result = OriginalAiStrategy.PrepareCorps(world, world.Corps.Single(c => c.Id == 1), state, geography,
            new OriginalAiRandom(maximum => { Assert.Equal(100, maximum); draws++; return 99; }));

        Assert.Equal(expected, result.Mode);
        Assert.Equal(expectedDraws, draws);
        Assert.Equal(expected == OriginalAiMode.TargetForce ? 2 : (int?)null, result.TargetForceId);
    }
}
