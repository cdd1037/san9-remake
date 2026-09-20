using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MysticBookTests
{
    private static TreasureRulebook Books => new([new(500, new(0, 10, 0, 0), 50) { TacticId = 29 }]);
    private static MysticBookOfferSearchEventDefinition Offer => new(1, 3, 500, 3, 1500, 500, 100)
        { Id = "offer", Priority = 1, Consumption = EventConsumption.None };
    private static MysticBookReturnSearchEventDefinition Return => new(1, 3, 500, 6, [26, 30, 29, 22])
        { Id = "return", Consumption = EventConsumption.None };
    private static SimulationRules Rules => new()
        { Treasures = Books, Personnel = new([new(1, new(0, 0)), new(2, new(0, 0))], searchEvents: [Offer, Return]) };

    private static (WorldMutation World, PersistentTask Task) Create(int roll = 0, int place = 1)
    {
        var world = WorldState.Create(new(190, 1, 1), 123,
            [new(1, 1501, 0, 1000) { LeaderId = 1, BaseTrust = 76 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(0, 0, 0, 99), false),
             new(3, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special },
             new(4, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }],
            treasures: [new(500, 3, TreasureDiscovery.Discovered, null)],
            controls: [new("player", 1, ControllerKind.Player)]).BeginMutation(new(Books, new([])));
        ulong seed = Enumerable.Range(1, 10000).Select(i => (ulong)i).First(s => new StableRandom(s).NextUInt64() % 100 == (ulong)roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("event-search-eligibility", seed)] });
        return (world, new(Guid.NewGuid(), "personnel-search", "player", 1, 2, 1, world.Clock, world.Clock, 0, 0)
            { Journey = new(place, null, 0) });
    }

    [Theory]
    [InlineData(false, "eligible", true, true)]
    [InlineData(true, "eligible", true, true)]
    [InlineData(false, "place", false, false)]
    [InlineData(true, "place", false, false)]
    [InlineData(false, "automatic", false, false)]
    [InlineData(true, "automatic", false, false)]
    [InlineData(false, "chance", false, true)]
    [InlineData(true, "chance", false, true)]
    [InlineData(false, "funds", false, true)]
    [InlineData(true, "funds", true, true)]
    [InlineData(false, "dead", false, true)]
    [InlineData(true, "free", false, true)]
    [InlineData(false, "holder", false, true)]
    [InlineData(true, "holder", false, true)]
    [InlineData(true, "all-known", false, true)]
    public void EligibilityUsesLeadershipAndKeepsProbabilityBeforeLateConditions(bool returning, string variant, bool expected, bool draws)
    {
        int threshold = returning ? 16 : 33;
        var (world, task) = Create(variant == "chance" ? threshold : threshold - 1);
        if (returning) world.Replace(world.Treasures[500] with { HolderId = 2 });
        switch (variant)
        {
            case "place": task = task with { Journey = new(2, null, 0) }; break;
            case "automatic": world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic)] }); break;
            case "funds": world.Replace(world.Forces[1] with { Money = 1500 }); break;
            case "dead": world.Replace(world.Officers[3] with { Status = OfficerStatus.Dead }); break;
            case "free": world.Replace(world.Officers[3] with { Status = OfficerStatus.Free }); break;
            case "holder": world.Replace(world.Treasures[500] with { HolderId = 4 }); break;
            case "all-known": world.Replace(world.Officers[2] with { LearnedTactics = Return.RewardTacticIds }); break;
        }
        var random = new StableRandom(world.Simulation.RandomStreams.Single().State); if (draws) random.NextUInt64();
        Assert.Equal(expected, returning ? MysticBook.Prepare(world, task, Return) : MysticBook.Prepare(world, task, Offer));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single().State);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 100)]
    [InlineData(true, 500)]
    [InlineData(false, 0)]
    [InlineData(false, 50)]
    [InlineData(false, 500)]
    public void OfferChargesTheRootAccountEvenOnRefusalAndSaturatesShortfalls(bool accept, uint rootMoney)
    {
        var (world, task) = Create();
        world.AddCorps(new(7, 1, 2, new(1501, 0)) { ControlMode = CorpsControlMode.Direct });
        world.Replace(world.Settlements[1] with { CorpsId = 7 }); world.Replace(world.Forces[1] with { Money = rootMoney });
        Assert.True(MysticBook.Prepare(world, task, Offer)); List<DomainEvent> events = [];
        MysticBook.Resolve(world, task, Rules, new MysticBookOfferEncounter(Offer.Id), accept, events);
        long cost = accept ? 500 : 100;
        var payment = Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal(new ResourceAccount(ResourceOwnerKind.Force, 1), payment.Account);
        Assert.Equal(-Math.Min(rootMoney, cost), payment.Actual); Assert.Equal(Math.Max(0, cost - rootMoney), payment.Shortfall);
        Assert.Equal(accept ? 2 : 3, world.Treasures[500].HolderId);
        Assert.Equal(1501u, world.Corps[7].Treasury!.Money); Assert.Empty(world.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void RefusingReturnLearnsOrderedPermanentTacticsIncludingTheBooksOwnTactic()
    {
        var (world, task) = Create(); world.Replace(world.Treasures[500] with { HolderId = 2 });
        foreach (int expected in new[] { 26, 30, 29, 22 })
        {
            Assert.True(MysticBook.CanResolve(world, task, Rules, new MysticBookReturnEncounter(Return.Id)));
            List<DomainEvent> events = [];
            MysticBook.Resolve(world, task, Rules, new MysticBookReturnEncounter(Return.Id), false, events);
            Assert.Equal(expected, Assert.Single(events.OfType<MysticBookReturnResolvedEvent>()).LearnedTacticId);
            Assert.Equal(2, world.Treasures[500].HolderId); Assert.DoesNotContain(events, e => e is ResourceChangedEvent);
        }
        Assert.False(MysticBook.CanResolve(world, task, Rules, new MysticBookReturnEncounter(Return.Id)));
        world.Replace(world.Treasures[500] with { HolderId = 3 });
        Assert.Equal(Return.RewardTacticIds.ToArray(), world.Officers[2].LearnedTactics.ToArray());
        Assert.Empty(world.Simulation.ConsumedEventKeys);
    }

    [Fact]
    public void ReturningTransfersTheBookWithoutRewardAndReopensTheOffer()
    {
        var (world, task) = Create(); world.Replace(world.Treasures[500] with { HolderId = 2 }); List<DomainEvent> events = [];
        MysticBook.Resolve(world, task, Rules, new MysticBookReturnEncounter(Return.Id), true, events);
        Assert.Equal(3, world.Treasures[500].HolderId); Assert.Empty(world.Officers[2].LearnedTactics);
        Assert.DoesNotContain(events, e => e is ResourceChangedEvent);
        Assert.True(MysticBook.CanResolve(world, task, Rules, new MysticBookOfferEncounter(Offer.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedHolderCancelsWithoutTransferPaymentOrLearning(bool returning)
    {
        var (world, task) = Create(); world.Replace(world.Treasures[500] with { HolderId = returning ? 2 : 3 });
        Assert.True(returning ? MysticBook.Prepare(world, task, Return) : MysticBook.Prepare(world, task, Offer));
        world.Replace(world.Treasures[500] with { HolderId = 4 }); List<DomainEvent> events = [];
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (returning) MysticBook.Resolve(world, task, Rules, new MysticBookReturnEncounter(Return.Id), false, events);
            else MysticBook.Resolve(world, task, Rules, new MysticBookOfferEncounter(Offer.Id), true, events);
        });
        Assert.Empty(events); Assert.Equal(1501u, world.Forces[1].Money); Assert.Empty(world.Officers[2].LearnedTactics);
    }
}
