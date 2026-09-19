using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class BeastSearchTests
{
    internal static BeastSearchEventDefinition Definition => new(
        [new(1, new("beast.home"), [9, 8, 7]), new(30, new("beast.other"), [7, 8, 9])],
        [1, 28], 8, 2, 2, 7, true)
        { Id = "beast", Consumption = EventConsumption.OnPresentation };
    private static PersonnelRulebook Rules(BeastSearchEventDefinition? definition = null) => new(
        [new(1, new(0, 0)), new(30, new(300, 0))], searchEvents: [definition ?? Definition]);
    private static ulong SeedForRoll(int roll) => Enumerable.Range(1, 10000).Select(i => (ulong)i)
        .First(seed => new StableRandom(seed).NextUInt64() % 100 == (ulong)roll);
    private static (WorldState World, SimulationEngine Engine) Create(int war = 99, bool knows = true, int battleRoll = 0,
        int lifespan = 3, bool loseLifespan = true)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 10000, 10000, 999) { LeaderId = 1, BaseTrust = 999 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(war, 50, 500, 50), false) { LifespanTier = lifespan, LearnedTactics = knows ? [1, 9] : [] },
             new(4, 1, 1, new(war, 50, 500, 50), false),
             new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered }],
            controls: [new("player", 1, ControllerKind.Player)]);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { RandomStreams =
            [new("event-beast-battle", SeedForRoll(battleRoll)), new("event-search-eligibility", SeedForRoll(0))] });
        world.Commit(mutation);
        return (world, new(world, EconomyRulebook.Empty, Rules(Definition with { ReduceLifespanOnDefeat = loseLifespan })));
    }
    private static BeastSearchPrompt Begin(SimulationEngine engine)
    {
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        engine.EndControl(new("player"));
        var result = engine.AdvanceXun();
        Assert.DoesNotContain(result.Events, e => e is BeastSearchResolvedEvent or SearchResolvedEvent);
        Assert.Null(engine.GetSearchRecruitmentPrompt());
        return Assert.IsType<BeastSearchPrompt>(engine.GetBeastSearchPrompt());
    }
    private static RespondDailyTaskCommand Answer(SimulationEngine engine, bool fight)
    {
        var prompt = engine.GetBeastSearchPrompt()!;
        return new(Guid.NewGuid(), prompt.ForceId, engine.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, fight);
    }

    [Fact]
    public void EscapeConsumesOnlySelectedSiteWithoutBattleOrOrdinarySearchAndAnswersOnlyOnce()
    {
        var (_, engine) = Create();
        var before = engine.Snapshot;
        var prompt = Begin(engine);
        Assert.Equal(19, prompt.BattleChance);
        string hash = engine.Snapshot.StateHash;
        for (int i = 0; i < 3; i++) Assert.Equal(prompt, engine.GetBeastSearchPrompt());
        Assert.Equal(hash, engine.Snapshot.StateHash);
        Assert.Empty(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
        Assert.Equal(99, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.War);
        var answer = Answer(engine, false);
        var response = engine.Execute(new("player"), answer);
        Assert.True(response.Submission.Accepted);
        var result = Assert.Single(response.Events.OfType<BeastSearchResolvedEvent>());
        Assert.Equal(EventResult.Escaped, result.Result);
        Assert.Null(result.Roll);
        Assert.Null(result.LearnedTacticId);
        Assert.Equal("beast.home", Assert.Single(engine.Snapshot.Simulation.ConsumedEventKeys).Value);
        Assert.Equal(before.Simulation.RandomStreams.Single(s => s.Name == "event-beast-battle"),
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "event-beast-battle"));
        Assert.DoesNotContain(response.Events, e => e is SearchResolvedEvent or OfficerAbilityGrownEvent);
        Assert.Equal(OfficerStatus.Undiscovered, engine.Snapshot.Officers.Single(p => p.Id == 3).Status);
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
        string after = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), answer).Submission.Accepted);
        Assert.Equal(after, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(18, EventResult.Succeeded)]
    [InlineData(19, EventResult.Defeated)]
    public void BattleUsesStrictChanceAndCommitsCappedRewardsOrInjury(int roll, EventResult expected)
    {
        var (_, engine) = Create(battleRoll: roll);
        Begin(engine);
        var response = engine.Execute(new("player"), Answer(engine, true));
        var result = Assert.Single(response.Events.OfType<BeastSearchResolvedEvent>());
        Assert.Equal(expected, result.Result);
        Assert.Equal(roll, result.Roll);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 2);
        if (expected == EventResult.Succeeded)
        {
            Assert.Equal(100, person.BaseAbilities.War);
            Assert.Equal(8, result.LearnedTacticId);
            Assert.Contains(8, person.LearnedTactics);
            Assert.DoesNotContain(7, person.LearnedTactics);
            Assert.Equal(1000, Assert.Single(engine.Snapshot.Forces).BaseTrust);
            Assert.Equal(3, person.LifespanTier);
            Assert.Equal(0, result.Injury);
        }
        else
        {
            Assert.Equal(99, person.BaseAbilities.War);
            Assert.Null(result.LearnedTacticId);
            Assert.Equal(7, result.Injury);
            Assert.Equal(2, result.LifespanTier);
            Assert.Contains(response.Events, e => e is OfficerInjuryRecoveredEvent { OfficerId: 2, Previous: 7, Current: 6 });
            Assert.Equal(999, Assert.Single(engine.Snapshot.Forces).BaseTrust);
        }
        Assert.Single(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.DoesNotContain(response.Events, e => e is SearchResolvedEvent);
    }

    [Theory]
    [InlineData(true, 3, 2)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 3, 3)]
    public void MissingBattleTacticStillRollsAndLifespanLossFollowsContent(bool loss, int life, int expected)
    {
        var (_, engine) = Create(knows: false, lifespan: life, loseLifespan: loss);
        Assert.Equal(0, Begin(engine).BattleChance);
        var response = engine.Execute(new("player"), Answer(engine, true));
        var result = Assert.Single(response.Events.OfType<BeastSearchResolvedEvent>());
        Assert.Equal(EventResult.Defeated, result.Result);
        Assert.Equal(0, result.Roll);
        Assert.Equal(expected, result.LifespanTier);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(49, 10)]
    [InlineData(50, 10)]
    [InlineData(55, 11)]
    [InlineData(100, 20)]
    public void BattleChanceHasContinuousLowerBound(int war, int expected)
    {
        var (world, _) = Create(war: war);
        Assert.Equal(expected, SearchEvents.BattleChance(world.BeginMutation(), 2, Definition));
    }

    [Fact]
    public void ConsumedSiteStillDrawsOneEligibilityRollThenContinuesOrdinarySearch()
    {
        var (world, engine) = Create();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { ConsumedEventKeys = [new("beast.home")] });
        world.Commit(mutation);
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        engine.EndControl(new("player"));
        engine.AdvanceXun();
        Assert.Null(engine.GetBeastSearchPrompt());
        Assert.NotNull(engine.GetSearchRecruitmentPrompt());
        var random = new StableRandom(SeedForRoll(0)); random.NextUInt64();
        Assert.Equal(random.State, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "event-search-eligibility").State);
    }

    [Fact]
    public void KnownRewardPoolStillGrantsWarAndTrustWithoutDuplicateTactic()
    {
        var (world, engine) = Create();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[2] with { LearnedTactics = [1, 9, 8, 7] });
        world.Commit(mutation);
        Begin(engine);
        var response = engine.Execute(new("player"), Answer(engine, true));
        Assert.Null(Assert.Single(response.Events.OfType<BeastSearchResolvedEvent>()).LearnedTacticId);
        Assert.DoesNotContain(response.Events, e => e is TacticLearnedEvent);
        Assert.Equal(100, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.War);
        Assert.Equal(1000, Assert.Single(engine.Snapshot.Forces).BaseTrust);
    }

    [Fact]
    public void SameDaySecondSearchCannotPresentAlreadyConsumedSite()
    {
        var (_, engine) = Create();
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 1)).Submission.Accepted);
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 1, 4, 1)).Submission.Accepted);
        engine.EndControl(new("player"));
        engine.AdvanceXun();
        Assert.Equal(2, engine.GetBeastSearchPrompt()!.OfficerId);
        var response = engine.Execute(new("player"), Answer(engine, false));
        Assert.Single(response.Events.OfType<BeastSearchResolvedEvent>());
        Assert.Null(engine.GetBeastSearchPrompt());
        Assert.Equal(4, engine.GetSearchRecruitmentPrompt()!.SearchingOfficerId);
        Assert.Single(engine.Snapshot.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void InvalidatedPreparedSiteCancelsWithoutRerollingOrPartialEffect()
    {
        var (world, engine) = Create();
        Begin(engine);
        var answer = Answer(engine, true);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[2] with { Injury = 5 });
        world.Commit(mutation);
        var response = engine.Execute(new("player"), answer);
        Assert.True(response.Submission.Accepted);
        Assert.Contains(response.Events, e => e is TaskCancelledEvent { Reason: "invalid-target-or-executor" });
        Assert.DoesNotContain(response.Events, e => e is BeastSearchResolvedEvent or SearchResolvedEvent);
        Assert.Empty(engine.Snapshot.Simulation.ConsumedEventKeys);
        Assert.Equal(SeedForRoll(0), engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "event-beast-battle").State);
        Assert.Equal(99, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.War);
    }
}
