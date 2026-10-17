using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SearchDecisionTests
{
    private static readonly PersonnelRulebook Places = new([new(1, new(0, 0))]);
    private static WorldState World(bool automatic = false, bool secondSearch = false) => WorldState.Create(new(190, 1, 1), 190,
        [new(1, 100) { LeaderId = 1 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        new[] {
            new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
            new(2, 1, 1, new(50, 50, 500, 50), false),
            new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered,
                ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 240) },
            new(4, 1, 1, new(50, 50, 500, 50), false),
        }.Concat(secondSearch ? [new OfficerState(5, null, 1, new(50, 50, 50, 50), false)
            { Status = OfficerStatus.Undiscovered, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 240) }] : []),
        controls: [new("player", 1, automatic ? ControllerKind.Automatic : ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }]);
    private static SimulationEngine Engine(WorldState world) => new(world, EconomyRulebook.Empty, Places);

    private static IReadOnlyList<DomainEvent> Begin(SimulationEngine engine, bool secondSearch = false)
    {
        Assert.True(engine.Execute(new("player"), new SearchCommand(SimulationEngineTests.Id(1), 1, 0, [2], 1)).Submission.Accepted);
        if (secondSearch) Assert.True(engine.Execute(new("player"), new SearchCommand(SimulationEngineTests.Id(2), 1, 1, [4], 1)).Submission.Accepted);
        engine.EndControl(new("player"));
        return engine.AdvanceXun().Events;
    }

    private static RespondDailyTaskCommand Answer(SimulationEngine engine, bool accept)
    {
        var prompt = engine.GetSearchRecruitmentPrompt()!;
        return new(SimulationEngineTests.Id(100 + (int)engine.Snapshot.Simulation.NextSequence), prompt.ForceId,
            engine.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, accept);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoiceCommitsDiscoveryOnlyOnceAndContinuesTheSuspendedDay(bool accept)
    {
        var engine = Engine(World());
        var events = Begin(engine);
        Assert.DoesNotContain(events, e => e is SearchResolvedEvent or PersonAffiliationChangedEvent);
        Assert.Equal(new GameClock(190, 1, 10), engine.Snapshot.Clock);
        Assert.Equal(SimulationBoundary.AwaitingDecision, engine.Snapshot.Simulation.Boundary);
        var target = engine.Snapshot.Officers.Single(o => o.Id == 3);
        Assert.Equal(OfficerStatus.Undiscovered, target.Status);
        Assert.Equal(240, target.LoyaltyTo(1));
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, r => r.Name == "personnel-search-recruitment");
        var before = engine.Snapshot;
        for (int query = 0; query < 3; query++) Assert.Equal(3, engine.GetSearchRecruitmentPrompt()!.DiscoveredOfficerId);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Throws<InvalidOperationException>(() => engine.CaptureStableState());
        var answer = Answer(engine, accept);
        var resolved = engine.Execute(new("player"), answer);
        Assert.True(resolved.Submission.Accepted);
        Assert.Equal(accept, Assert.Single(resolved.Events.OfType<SearchResolvedEvent>()).Recruited);
        Assert.Equal(accept ? OfficerStatus.Serving : OfficerStatus.Free, engine.Snapshot.Officers.Single(o => o.Id == 3).Status);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(accept, engine.Snapshot.Simulation.RandomStreams.Any(r => r.Name == "personnel-search-recruitment"));
        var after = engine.Snapshot;
        Assert.False(engine.Execute(new("player"), answer).Submission.Accepted);
        StateAssert.Equal(after, engine.Snapshot);
    }

    [Fact]
    public void WrongTaskStepControllerAndUnrelatedCommandsDoNotConsumeChoice()
    {
        var engine = Engine(World());
        Begin(engine);
        var answer = Answer(engine, true);
        var before = engine.Snapshot;
        Assert.False(engine.Execute(new("player"), answer with { TaskId = Guid.NewGuid() }).Submission.Accepted);
        Assert.False(engine.Execute(new("player"), answer with { Step = 0 }).Submission.Accepted);
        Assert.False(engine.Execute(new("other"), answer).Submission.Accepted);
        Assert.False(engine.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, answer.Sequence)).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameDaySearchesSelectAndCommitInQueueOrderWithoutReusingDiscoveredPerson(bool batch)
    {
        var engine = Engine(World(secondSearch: true));
        if (batch)
        {
            Assert.True(engine.Execute(new("player"),new SearchCommand(SimulationEngineTests.Id(1),1,0,[4,2],1)).Submission.Accepted);
            engine.EndControl(new("player"));
            engine.AdvanceXun();
        }
        else Begin(engine, true);
        var first = engine.GetSearchRecruitmentPrompt()!;
        Assert.Equal(2, first.SearchingOfficerId);
        var resolved = engine.Execute(new("player"), Answer(engine, false));
        Assert.True(resolved.Submission.Accepted);
        Assert.Single(resolved.Events.OfType<SearchResolvedEvent>());
        var second = engine.GetSearchRecruitmentPrompt()!;
        Assert.Equal(4, second.SearchingOfficerId);
        Assert.NotEqual(first.DiscoveredOfficerId, second.DiscoveredOfficerId);
        Assert.Equal(new GameClock(190, 1, 10), engine.Snapshot.Clock);
        Assert.True(engine.Execute(new("player"), Answer(engine, false)).Submission.Accepted);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.Null(engine.GetSearchRecruitmentPrompt());
    }

    [Fact]
    public void AutomaticAndPlayerAcceptanceUseSameRuleRandomAndDomainEffects()
    {
        var player = Engine(World());
        Begin(player);
        var playerEvents = player.Execute(new("player"), Answer(player, true)).Events;
        var automatic = Engine(World(automatic: true));
        Begin(automatic);
        Assert.NotNull(automatic.GetSearchRecruitmentPrompt());
        var automaticEvents = automatic.Execute(new("player"), Answer(automatic, true)).Events;
        Assert.Null(automatic.GetSearchRecruitmentPrompt());
        Assert.Equal(CanonicalJson.Text(player.Snapshot.Officers), CanonicalJson.Text(automatic.Snapshot.Officers));
        Assert.Equal(CanonicalJson.Text(player.Snapshot.Simulation.RandomStreams), CanonicalJson.Text(automatic.Snapshot.Simulation.RandomStreams));
        Assert.Equal(Assert.Single(playerEvents.OfType<SearchResolvedEvent>()), Assert.Single(automaticEvents.OfType<SearchResolvedEvent>()));
    }

    [Fact]
    public void LostTargetCancelsPreparedEncounterWithoutInterestRewardOrRetargeting()
    {
        var world = World(secondSearch: true);
        var engine = Engine(world);
        Begin(engine);
        var prompt = engine.GetSearchRecruitmentPrompt()!;
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[prompt.DiscoveredOfficerId] with { Status = OfficerStatus.Free });
        world.Commit(mutation);
        var resolved = engine.Execute(new("player"), Answer(engine, true));
        Assert.True(resolved.Submission.Accepted);
        Assert.Contains(resolved.Events, e => e is TaskCancelledEvent { Reason: "invalid-target-or-executor" });
        Assert.DoesNotContain(resolved.Events, e => e is SearchResolvedEvent or OfficerLoyaltyChangedEvent);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, r => r.Name == "personnel-search-recruitment");
        Assert.Single(engine.Snapshot.Officers, o => o.Status == OfficerStatus.Undiscovered);
    }
}
