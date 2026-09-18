using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SearchOutcomeTests
{
    private static SimulationEngine Engine(bool person, bool item, bool automatic = false, int politics = 500, ulong seed = 190,
        bool probeGrowth = false, ulong fundsSeed = 52)
    {
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 10000, 10000, 0) { LeaderId = 1 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 50, politics, 50), false),
             new(3, null, null, new(50, 50, 50, 50), false) { Status = person ? OfficerStatus.Undiscovered : OfficerStatus.Dead,
                 RegionId = 30, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 40) }],
            treasures: item ? [new(10, null, TreasureDiscovery.Buried, 30)] : [],
            controls: [new("player", 1, automatic ? ControllerKind.Automatic : ControllerKind.Player)]);
        if (probeGrowth)
        {
            var mutation = world.BeginMutation();
            mutation.SetSimulation(mutation.Simulation with { RandomStreams =
                [new("personnel-politics-growth", 52), new("personnel-search-funds", fundsSeed)] });
            world.Commit(mutation);
        }
        return new(world, EconomyRulebook.Empty, new([new(1, new(0, 0)), new(30, new(300, 0))]),
            item ? new([new(10, new(5, 0, 0, 0), 0)]) : TreasureRulebook.Empty);
    }

    private static SearchResolvedEvent Search(SimulationEngine engine)
    {
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 30)
            { AcceptRecruitment = false }).Submission.Accepted);
        engine.EndControl(new("player"));
        return Assert.Single(engine.AdvanceXun().Events.OfType<SearchResolvedEvent>());
    }

    [Fact]
    public void DecliningDiscoveredPersonKeepsInterestAndDoesNotDrawRecruitmentOrLowerPriorityResults()
    {
        var engine = Engine(true, true);
        Assert.Equal(SearchResultKind.Person, Search(engine).Result);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 3);
        Assert.Equal(OfficerStatus.Free, person.Status);
        Assert.Equal(60, person.LoyaltyTo(1));
        Assert.Equal(TreasureDiscovery.Buried, Assert.Single(engine.Snapshot.Treasures).Discovery);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams,
            s => s.Name is "personnel-search-recruitment" or "personnel-search-treasure" or "personnel-search-funds");
        Assert.Single(engine.Snapshot.Simulation.Tasks.Where(t => t.Kind == "personnel-return"));
    }

    [Fact]
    public void TreasurePrecedesMoneyAndBecomesUniqueRulerHoldingWithAbilityBonus()
    {
        var engine = Engine(false, true);
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Treasure, result.Result);
        Assert.Equal(10, result.TreasureId);
        Assert.Equal(1, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(engine.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.War + 5, engine.GetAbilities(engine.Snapshot)[1].War);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "personnel-search-funds");
    }

    [Fact]
    public void AutomaticSearchUsesLimitedResultsAndCreditsFundsWithoutPoliticsGrowth()
    {
        var engine = Engine(false, true, true);
        uint before = Assert.Single(engine.Snapshot.Forces).Money;
        var result = Search(engine);
        Assert.Equal(SearchResultKind.Money, result.Result);
        Assert.True(result.Money > 0);
        Assert.Equal((long)before + result.Money - 1, Assert.Single(engine.Snapshot.Forces).Money); // Normal xun salary remains payable.
        Assert.Null(Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name is "personnel-search-treasure" or "personnel-politics-growth");
    }

    [Fact]
    public void EmptySearchCanFinishWithoutRewardAndStillReturn()
    {
        var engine = Engine(false, false, politics: 1);
        uint before = Assert.Single(engine.Snapshot.Forces).Money;
        Assert.Equal(SearchResultKind.None, Search(engine).Result);
        Assert.Equal(before - 1, Assert.Single(engine.Snapshot.Forces).Money);
        Assert.Single(engine.Snapshot.Simulation.Tasks);
    }

    [Theory]
    [InlineData(SearchResultKind.Person, 99)]
    [InlineData(SearchResultKind.Treasure, 99)]
    [InlineData(SearchResultKind.Money, 99)]
    [InlineData(SearchResultKind.None, 99)]
    [InlineData(SearchResultKind.Person, 100)]
    [InlineData(SearchResultKind.Treasure, 120)]
    public void SearchPoliticsGrowthBelongsOnlyToPersonAndTreasureResults(SearchResultKind expected, int politics)
    {
        ulong seed = Enumerable.Range(0, 1000).Select(value => (ulong)value).First(value =>
            ConditionalRandom.Percentile(value, ConditionalRandomPurpose.SearchDiscovery, new(190, 1, 1), 2, 30) == 0);
        var engine = Engine(expected == SearchResultKind.Person, expected == SearchResultKind.Treasure,
            politics: politics, seed: seed, probeGrowth: true, fundsSeed: expected == SearchResultKind.None ? 18UL : 52UL);

        Assert.Equal(expected, Search(engine).Result);

        bool grows = politics < 100 && expected is SearchResultKind.Person or SearchResultKind.Treasure;
        Assert.Equal(grows ? 100 : politics, engine.Snapshot.Officers.Single(p => p.Id == 2).BaseAbilities.Politics);
        var random = new StableRandom(52);
        random.NextUInt64();
        Assert.Equal(grows ? random.State : 52UL,
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "personnel-politics-growth").State);
        Assert.Equal(50, engine.Snapshot.Officers.Single(p => p.Id == 1).BaseAbilities.Politics);
    }
}
