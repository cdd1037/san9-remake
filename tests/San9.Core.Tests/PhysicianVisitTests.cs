using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PhysicianVisitTests
{
    private static TreasureRulebook Books => new([new(500, new(0, 10, 0, 0), 50) { TacticId = 900 },
        new(501, new(0, 10, 0, 0), 50) { TacticId = 900 }]);
    private static PhysicianVisitSearchEventDefinition Definition => new([new(1, 3, 500), new(2, 4, 501)], 6, 75)
        { Id = "physician", Consumption = EventConsumption.None };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0, int place = 1)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 1000) { LeaderId = 1, BaseTrust = 76 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 99, 99, 50), false),
             new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }],
            treasures: [new(500, 3, TreasureDiscovery.Discovered, null), new(501, 4, TreasureDiscovery.Discovered, null)],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(0, 0)) { IsRoot = true }]).BeginMutation(new(Books, new([])));
        ulong seed = TestSeeds.StateForRoll(roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(place, null, 0) });
    }

    [Theory]
    [InlineData("eligible", true, true)]
    [InlineData("place", false, false)]
    [InlineData("automatic", false, false)]
    [InlineData("trust", false, true)]
    [InlineData("chance", false, true)]
    [InlineData("dead", false, true)]
    [InlineData("free", false, true)]
    [InlineData("holder", false, true)]
    [InlineData("buried", false, true)]
    public void OnlyCorrectPlaceSpecialPhysicianAndCurrentHoldingCanProduceGift(string variant, bool expected, bool draws)
    {
        var (world, task) = Create(variant == "chance" ? 16 : 15);
        switch (variant)
        {
            case "place": task = task with { Journey = new(9, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "trust": world.Replace(world.Forces[1] with { BaseTrust = 75 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
            case "holder": world.Replace(world.Treasures[500] with { HolderId = 4 }); break;
            case "buried": world.Replace(world.Treasures[500] with { HolderId = null, Discovery = TreasureDiscovery.Buried, DiscoveryRegionId = 1 }); break;
        }
        var before = world.Treasures[500]; var actor = world.Officers[2]; var force = world.Forces[1];
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State);
        if (draws) random.NextUInt64();
        var events = new List<DomainEvent>();
        Assert.Equal(expected, PhysicianVisit.TryResolve(world, task, Definition, EconomyRulebook.Empty, events));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
        Assert.Equal(force, world.Forces[1]); Assert.Equal(actor, world.Officers[2]); Assert.Empty(world.Simulation.ConsumedEventKeys);
        if (expected)
        {
            Assert.Equal(before with { HolderId = 2 }, world.Treasures[500]);
            Assert.Equal((3, 2), (Assert.Single(events.OfType<TreasureTransferredEvent>()).PreviousHolderId,
                Assert.Single(events.OfType<TreasureTransferredEvent>()).HolderId));
            Assert.Equal(500, Assert.Single(events.OfType<PhysicianVisitResolvedEvent>()).TreasureId);
        }
        else { Assert.Equal(before, world.Treasures[500]); Assert.Empty(events); }
    }

    [Theory]
    [InlineData(1, 500, 3)]
    [InlineData(2, 501, 4)]
    public void BothBooksProvideTemporaryTacticsAndCanBeGivenAgainOnlyAfterReturningToDonor(int place, int item, int donor)
    {
        var (world, task) = Create(place: place); var random = world.Simulation.RandomStreams;
        var treasures = Books; var beforeAbilities = world.Abilities(world.Officers[2]);
        Assert.False(MilitaryTactics.HasTactic(world, 1, [2], 900, treasures));
        Assert.True(PhysicianVisit.TryResolve(world, task, Definition, EconomyRulebook.Empty, []));
        Assert.True(MilitaryTactics.HasTactic(world, 1, [2], 900, treasures));
        Assert.True(world.Abilities(world.Officers[2]).Intelligence > beforeAbilities.Intelligence);
        Assert.Empty(world.Officers[2].LearnedTactics);
        world.SetSimulation(world.Simulation with { RandomStreams = random });
        Assert.False(PhysicianVisit.TryResolve(world, task, Definition, EconomyRulebook.Empty, []));
        PersonnelTreasures.Transfer(world, world.Treasures[item], donor, 1, "test-return", []);
        Assert.False(MilitaryTactics.HasTactic(world, 1, [2], 900, treasures));
        Assert.Equal(beforeAbilities, world.Abilities(world.Officers[2]));
        world.SetSimulation(world.Simulation with { RandomStreams = random });
        Assert.True(PhysicianVisit.TryResolve(world, task, Definition, EconomyRulebook.Empty, []));
        Assert.Equal(2, world.Treasures.Count); Assert.Equal(2, world.Treasures[item].HolderId);
    }
}
