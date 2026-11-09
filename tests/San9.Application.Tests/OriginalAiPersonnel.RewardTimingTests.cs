using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiPersonnelTests
{
    private static ScenarioInitialState RewardTimingContent()
    {
        var basis = Fixture();
        return basis with
        {
            Forces = basis.Forces.Select(f => f.Id == 1 ? f with { AutomaticSchedulingKey = 40 } : f).ToArray(),
            Settlements = [basis.Settlements[0], basis.Settlements[1], basis.Settlements[0] with { Id = 3, CorpsId = 3 }],
            Officers = basis.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State.WithLoyalty(1, 80) with
                { SettlementId = o.State.Id == 3 ? 3 : 1 } } : o).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps,
                new(3, 1, 3, new(10000, 50000))] },
            OriginalAi = basis.OriginalAi with
            {
                WorkRules = basis.OriginalAi.WorkRules.ToImmutableDictionary(p => p.Key,
                    p => p.Key == OriginalAiWork.Reward ? OriginalAiDefinition.Default.WorkRules[p.Key]
                        : p.Value with { OpeningWaitDays = 10000 }),
                SecondaryBatches = basis.OriginalAi.SecondaryBatches.ToImmutableDictionary(p => p.Key,
                    _ => ImmutableArray<OriginalAiWork>.Empty),
            },
        };
    }

    [Theory]
    [InlineData(GameDifficulty.Beginner, 1)]
    [InlineData(GameDifficulty.Advanced, 4)]
    public void ComputerRewardTimingOptionRemovesOnlyDateGates(GameDifficulty difficulty, int month)
    {
        var content = RewardTimingContent();
        var session = GameSession.Start(content, 190, playerForceController: ControllerKind.Automatic,
            settings: new() { Difficulty = difficulty });
        var world = session.Snapshot with
        {
            Clock = new(190, month, 1),
            Corps = session.Snapshot.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(2300, 50000) } : c).ToArray(),
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(world));
        var definition = new ScenarioSessionDefinition(content);
        var state = session.OriginalAi;
        List<DomainEvent> events = [];
        List<OriginalAiWorkReport> reports = [];
        void Run()
        {
            events.Clear();
            reports.Clear();
            state = OriginalAiTurnRunner.Run(engine, definition, world.Simulation.Controls[0], state, events, reports, new());
        }

        Run();
        Assert.Equal(OriginalAiWorkEnd.Calendar, Assert.Single(reports, r => r.Work == OriginalAiWork.Reward).End);
        StateAssert.Equal(world, engine.Snapshot);

        engine.ConfigureCompanion(new() { DisableComputerRewardTimingRestrictions = true });
        Run();
        var reward = Assert.Single(reports, r => r.Work == OriginalAiWork.Reward);
        Assert.Equal(1, reward.CorpsId);
        Assert.Equal(2, reward.Submitted);
        Assert.All(reports.Where(r => r.Work != OriginalAiWork.Reward && r.End != OriginalAiWorkEnd.Permission),
            r => Assert.Equal(OriginalAiWorkEnd.Calendar, r.End));
        Assert.Equal(new[] { 2, 3 }, events.OfType<OfficerRewardedEvent>().Select(e => e.OfficerId).Order());
        Assert.Equal(2100u, engine.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        Assert.Equal(world.Corps.Single(c => c.Id == 3).Treasury, engine.Snapshot.Corps.Single(c => c.Id == 3).Treasury);
        var before = engine.Snapshot;
        Run();
        Assert.Empty(events.OfType<OfficerRewardedEvent>());
        StateAssert.Equal(before, engine.Snapshot);
        engine.ConfigureCompanion(CompanionSettings.Default);
        Run();
        Assert.Equal(OriginalAiWorkEnd.Calendar, Assert.Single(reports, r => r.Work == OriginalAiWork.Reward).End);
    }

    [Fact]
    public void RewardTimingOptionDoesNotAutomatePlayerOrDelegatedChildRewards()
    {
        var content = RewardTimingContent();
        var session = GameSession.Start(content, 190,
            companionSettings: new() { DisableComputerRewardTimingRestrictions = true });
        var before = session.Snapshot;
        var result = session.EndControl(new("player"));
        Assert.DoesNotContain(result.Events, e => e is OfficerRewardedEvent);
        Assert.DoesNotContain(session.LastOriginalAiWork, r => r.Work == OriginalAiWork.Reward);
        Assert.Contains(session.LastOriginalAiWork, r => r.CorpsId == 3);
        foreach (int id in new[] { 2, 3 })
            Assert.Equal(before.Officers.Single(p => p.Id == id), session.Snapshot.Officers.Single(p => p.Id == id));
    }
}
