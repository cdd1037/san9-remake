using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ScholarReturnTests
{
    internal static ScholarReturnSearchEventDefinition Definition => new(3, 1, new(195, 1, 1), new(207, 12, 30),
        115, 3, 300, 1000, 3000, 2, 2, 2, [10, 25, 22, 29], new("scholar-return"), new("scholar-return.accepted"))
        { Id = "scholar-return", Consumption = EventConsumption.OnPresentation };
    private static ulong Seed(int roll) => Enumerable.Range(1, 10000).Select(i => (ulong)i)
        .First(seed => new StableRandom(seed).NextUInt64() % 100 == (ulong)roll);
    private static (WorldState World, SimulationEngine Engine, PersonnelRulebook Rules) Create(int intelligence = 99, int politics = 99,
        int roll = 0, uint money = 10000, int trust = 999, GameClock? clock = null)
    {
        var world = WorldState.Create(clock ?? new(195, 1, 1), 123,
            [new(1, money, 10000, trust) { LeaderId = 1, BaseTrust = trust }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, intelligence, politics, 50), false),
             new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, 1, 1, new(50, 99, 99, 50), false)], controls: [new("player", 1, ControllerKind.Player)]);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(roll))] });
        world.Commit(mutation);
        var rules = new PersonnelRulebook([new(1, new(0, 0)), new(30, new(0, 0))], searchEvents: [Definition]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, rules);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { Money = money, Trust = trust, BaseTrust = trust });
        world.Commit(mutation);
        return (world, engine, rules);
    }
    private static ScholarReturnPrompt Begin(SimulationEngine engine)
    {
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        engine.EndControl(new("player")); engine.AdvanceXun();
        return Assert.IsType<ScholarReturnPrompt>(engine.GetScholarReturnPrompt());
    }
    private static RespondDailyTaskCommand Answer(SimulationEngine engine, bool accept)
    {
        var p = engine.GetScholarReturnPrompt()!;
        return new(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, p.TaskId, p.Step, accept);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnswerCommitsOnceWithoutPaymentOrScholarTransfer(bool accept)
    {
        var (_, engine, _) = Create();
        Begin(engine);
        string before = engine.Snapshot.StateHash;
        var scholar = engine.Snapshot.Officers.Single(p => p.Id == 3);
        var force = Assert.Single(engine.Snapshot.Forces);
        Assert.Empty(force.EventHistory); Assert.Empty(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
        for (int i = 0; i < 3; i++) Assert.NotNull(engine.GetScholarReturnPrompt());
        Assert.Equal(before, engine.Snapshot.StateHash);
        var command = Answer(engine, accept);
        var response = engine.Execute(new("player"), command);
        Assert.True(response.Submission.Accepted);
        Assert.Equal(accept, Assert.Single(response.Events.OfType<ScholarReturnResolvedEvent>()).Accepted);
        Assert.DoesNotContain(response.Events, e => e is SearchResolvedEvent);
        var after = Assert.Single(engine.Snapshot.Forces);
        var moneyChanges = response.Events.OfType<ResourceChangedEvent>().Where(e => e.Resource == ResourceKind.Money).ToArray();
        Assert.All(moneyChanges, e => Assert.Equal("salary", e.Reason));
        Assert.Equal((long)force.Money + moneyChanges.Sum(e => e.Actual), after.Money);
        Assert.Equal(scholar, engine.Snapshot.Officers.Single(p => p.Id == 3));
        Assert.Equal(accept, after.EventHistory.Contains(Definition.SuccessHistoryKey));
        Assert.Equal(accept ? 1000 : 999, after.BaseTrust);
        Assert.Equal(accept ? 100 : 99, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.Intelligence);
        Assert.Equal(Definition.ConsumptionKey, Assert.Single(engine.Snapshot.Simulation.ConsumedEventKeys));
        before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(114)]
    public void EveryTacticCombinationUsesFirstUnknownOrCappedPolitics(int ability)
    {
        for (int mask = 0; mask < 16; mask++)
        {
            var (world, engine, _) = Create(ability, ability);
            var known = Definition.RewardTacticIds.Where((_, i) => (mask & (1 << i)) != 0).ToImmutableArray();
            var mutation = world.BeginMutation();
            mutation.Replace(mutation.Officers[2] with { LearnedTactics = known }); world.Commit(mutation);
            Begin(engine);
            var response = engine.Execute(new("player"), Answer(engine, true));
            int? expected = Definition.RewardTacticIds.Where(id => !known.Contains(id)).Select(id => (int?)id).FirstOrDefault();
            Assert.Equal(expected, Assert.Single(response.Events.OfType<ScholarReturnResolvedEvent>()).LearnedTacticId);
            var actor = engine.Snapshot.Officers.Single(p => p.Id == 2);
            Assert.Equal(ability >= 100 ? ability : 100, actor.BaseAbilities.Intelligence);
            Assert.Equal(expected is null && ability < 100 ? 100 : ability, actor.BaseAbilities.Politics);
            Assert.Equal(known.Length + (expected is null ? 0 : 1), actor.LearnedTactics.Length);
        }
    }

    [Theory]
    [InlineData("consumed", false)]
    [InlineData("intelligence", false)]
    [InlineData("politics", false)]
    [InlineData("place", false)]
    [InlineData("early", true)]
    [InlineData("late", true)]
    [InlineData("trust", true)]
    [InlineData("base-trust", true)]
    [InlineData("money", true)]
    [InlineData("dead", true)]
    public void FailedEligibilityPreservesConsumptionAndRespectsRandomPosition(string failure, bool draws)
    {
        var (world, _, _) = Create();
        var m = world.BeginMutation();
        int place = 1;
        switch (failure)
        {
            case "consumed": m.SetSimulation(m.Simulation with { ConsumedEventKeys = [Definition.ConsumptionKey] }); break;
            case "intelligence": m.Replace(m.Officers[2] with { BaseAbilities = new(50, 115, 99, 50) }); break;
            case "politics": m.Replace(m.Officers[2] with { BaseAbilities = new(50, 99, 115, 50) }); break;
            case "place": place = 30; break;
            case "early": m.SetClock(new(194, 12, 30)); break;
            case "late": m.SetClock(new(208, 1, 1)); break;
            case "trust": m.Replace(m.Forces[1] with { Trust = 300 }); break;
            case "base-trust": m.Replace(m.Forces[1] with { BaseTrust = 1000 }); break;
            case "money": m.Replace(m.Forces[1] with { Money = 3000 }); break;
            case "dead": m.Replace(m.Officers[3] with { Status = OfficerStatus.Dead }); break;
        }
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, m.Clock, m.Clock, 0, 0)
            { Journey = new(place, null, 0) };
        var keys = m.Simulation.ConsumedEventKeys;
        var random = new StableRandom(Seed(0)); if (draws) random.NextUInt64();
        Assert.False(ScholarReturn.Prepare(m, task, Definition));
        Assert.Equal(random.State, m.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
        Assert.Equal(keys, m.Simulation.ConsumedEventKeys);
    }

    [Theory]
    [InlineData(195, 1, 1, 3001, 301, 32, true)]
    [InlineData(207, 12, 30, 3001, 301, 32, true)]
    [InlineData(195, 1, 1, 3001, 301, 33, false)]
    public void InclusiveDatesStrictMoneyTrustAndProbability(int year, int month, int day, uint money, int trust, int roll, bool expected)
    {
        var (world, _, _) = Create(roll: roll, money: money, trust: trust);
        var m = world.BeginMutation();
        m.SetClock(new(year, month, day));
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, m.Clock, m.Clock, 0, 0)
            { Journey = new(1, null, 0) };
        Assert.Equal(expected, ScholarReturn.Prepare(m, task, Definition));
    }

    [Fact]
    public void InvalidatedScholarAbortsWithoutRewardsOrConsumption()
    {
        var (world, engine, _) = Create(); Begin(engine);
        var answer = Answer(engine, true);
        var m = world.BeginMutation();
        m.Replace(m.Officers[3] with { Status = OfficerStatus.Dead }); world.Commit(m);
        var result = engine.Execute(new("player"), answer);
        Assert.DoesNotContain(result.Events, e => e is ScholarReturnResolvedEvent);
        Assert.Empty(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Empty(Assert.Single(engine.Snapshot.Forces).EventHistory);
        Assert.Equal(99, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.Intelligence);
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 3).Status);
    }

    [Fact]
    public void EligibilityFailureCanRetryAndValidationDoesNotReroll()
    {
        var (world, _, rules) = Create(roll: 40, trust: 301);
        var m = world.BeginMutation();
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, m.Clock, m.Clock, 0, 0)
            { Journey = new(1, null, 0) };
        Assert.False(ScholarReturn.Prepare(m, task, Definition));
        Assert.Empty(m.Simulation.ConsumedEventKeys);
        m.SetSimulation(m.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(0))] });
        Assert.True(ScholarReturn.Prepare(m, task, Definition));
        var selectedRandom = m.Simulation.RandomStreams;
        Assert.True(ScholarReturn.CanResolve(m, task, rules, new(Definition.Id)));
        Assert.Equal(selectedRandom, m.Simulation.RandomStreams);
    }

    [Fact]
    public void ChanceUsesEffectivePoliticsAfterInjuryRatherThanBasePolitics()
    {
        var (world, _, _) = Create(roll: 20);
        var m = world.BeginMutation();
        m.Replace(m.Officers[2] with { Injury = 5 });
        var task = new PersistentTask(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, m.Clock, m.Clock, 0, 0)
            { Journey = new(1, null, 0) };
        Assert.False(ScholarReturn.Prepare(m, task, Definition));
        m.Replace(m.Officers[2] with { Injury = 0 });
        m.SetSimulation(m.Simulation with { RandomStreams = [new("event-search-eligibility", Seed(20))] });
        Assert.True(ScholarReturn.Prepare(m, task, Definition));
    }

    [Fact]
    public void SameDaySecondActorCannotRepeatConsumedEvent()
    {
        var (_, engine, _) = Create();
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 1, 4, 1)).Submission.Accepted);
        engine.EndControl(new("player")); engine.AdvanceXun();
        var response = engine.Execute(new("player"), Answer(engine, false));
        Assert.Single(response.Events.OfType<ScholarReturnResolvedEvent>());
        Assert.Null(engine.GetScholarReturnPrompt());
        Assert.Single(response.Events.OfType<SearchResolvedEvent>());
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
    }
}
