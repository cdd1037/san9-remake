using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SunMarriageTests
{
    private static SunMarriageDefinition Definition() => new(11, 22, 900, 3, new(209, 12, 30), 70, 20, 150, [13, 1], new("sun-marriage"));
    private static WorldState World(bool player = true) => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21, AdviserId = 22 },
         new(3, 100) { LeaderId = 31 }],
        Enumerable.Range(1, 4).Select(id => new SettlementState(id, id == 4 ? 2 : id, 100, 100, 100, 50, 100, 100, 100, 100, 100)
            { Kind = SettlementKind.City, ConsumesFood = false }),
        [new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, BloodlineId = 900 },
         new OfficerState(22, 2, 4, new(50, 50, 50, 50), false) { Status = OfficerStatus.Strategist, TacticCooldown = 7 }.WithLoyalty(2, 95),
         new(23, 2, 4, new(50, 50, 50, 50), false),
         new(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        treasures: [new(7, 22, TreasureDiscovery.Discovered, null)],
        controls: [new("first", 1, player ? ControllerKind.Player : ControllerKind.Automatic), new("second", 2, ControllerKind.Automatic)],
        relations: [new(1, 2, 71), new(1, 3, 50), new(2, 3, 50)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);
    private static SimulationRules Rules(SunMarriageDefinition? definition = null) => new()
    {
        SunMarriage = definition ?? Definition(),
        Treasures = new([new(7, default, 0)]),
        Personnel = new(Enumerable.Range(1, 4).Select(id => new PersonPlace(id, new(id * 5 + 2, 2)))),
        Military = new(new(10, 1, Enumerable.Repeat(0, 10).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 5, 0, 10000, 2, new(1, 1), TravelMode.Land)],
            Enumerable.Range(1, 4).Select(id => new BattlefieldPlace(id, new(id * 5 + 2, 2), new(1, 1))).ToImmutableArray()))
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothControllersTransferAndConsumeButOnlyPlayerRecipientLearns(bool player)
    {
        var state = World(player); var world = state.BeginMutation(Rules().Abilities); List<DomainEvent> events = [];
        SunMarriage.Apply(world, Rules(), events);
        var result = Assert.Single(events.OfType<SunMarriageEvent>());
        Assert.Equal(player, result.GrantedTactics);
        Assert.Equal((1, 1, OfficerStatus.Serving, 150), (world.Officers[22].OwnerId, world.Officers[22].SettlementId, world.Officers[22].Status, world.Officers[22].Loyalty));
        Assert.Equal(95, world.Officers[22].LoyaltyTo(2)); Assert.Equal(0, world.Officers[22].TacticCooldown);
        Assert.Null(world.Forces[2].AdviserId); Assert.Equal(22, world.Treasures[7].HolderId);
        Assert.Equal(91, ForceRelations.Get(world, 1, 2).Value);
        Assert.Equal(player ? new[] { 13, 1 } : [], world.Officers[11].LearnedTactics);
        Assert.Equal(player ? new[] { 13, 1 } : [], world.Officers[22].LearnedTactics);
        Assert.Empty(world.Simulation.RandomStreams); state.Commit(world);
        world = state.BeginMutation(Rules().Abilities); SunMarriage.Apply(world, Rules(), events);
        Assert.Single(events.OfType<SunMarriageEvent>());
    }

    [Theory]
    [InlineData("relation", 70)]
    [InlineData("relation", 0)]
    [InlineData("captive", 0)]
    [InlineData("ruler", 0)]
    [InlineData("wrong-family", 0)]
    [InlineData("wrong-owner", 0)]
    [InlineData("two-forces", 0)]
    [InlineData("late", 0)]
    public void FailedQualificationPreservesEffectsAndConsumption(string condition, int value)
    {
        var world = World().BeginMutation(Rules().Abilities);
        switch (condition)
        {
            case "relation": ForceRelations.Set(world, 1, 2, value, []); break;
            case "captive": world.Replace(world.Officers[22] with { Status = OfficerStatus.Captive }); break;
            case "ruler": world.Replace(world.Officers[22] with { Status = OfficerStatus.Ruler }); break;
            case "wrong-family": world.Replace(world.Officers[21] with { BloodlineId = 901 }); break;
            case "wrong-owner": world.Replace(world.Officers[22] with { OwnerId = 3 }); break;
            case "two-forces": world.Replace(world.Forces[3] with { IsEliminated = true }); break;
            case "late": world.SetClock(new(210, 1, 1)); break;
        }
        List<DomainEvent> events = []; SunMarriage.Apply(world, Rules(), events);
        Assert.Empty(events); Assert.Empty(world.Simulation.ConsumedEventKeys); Assert.Empty(world.Officers[11].LearnedTactics);
    }

    [Fact]
    public void GovernorTransferDissolvesOldCorpsEvenWithRemainingMembersAndReturnsItsResources()
    {
        var state = World(); var world = state.BeginMutation(Rules().Abilities);
        { world.Replace(world.Forces[2] with { AdviserId = null }); world.Replace(ResourceAccounts.Root(world, 2) with { Treasury = ResourceAccounts.Root(world, 2).Treasury with { Money = 999999 } }); }
        world.Replace(world.Officers[22] with { Status = OfficerStatus.Governor });
        var rootTreasury20 = ResourceAccounts.Root(world, 2).Treasury; world.RemoveCorps(ResourceAccounts.Root(world, 2).Id); world.AddCorps(new(20, 2, 21, rootTreasury20) { IsRoot = true }); world.AddCorps(new(24, 2, 22, new(100, 200)));
        world.Replace(world.Settlements[2] with { CorpsId = 20 }); world.Replace(world.Settlements[4] with { CorpsId = 24 });
        List<DomainEvent> events = []; SunMarriage.Apply(world, Rules(), events);
        Assert.False(world.Corps.ContainsKey(24)); Assert.Equal(20, world.Settlements[4].CorpsId);
        Assert.Equal(2, world.Officers[23].OwnerId); Assert.Equal(1000000u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        Assert.Equal(10200u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food); Assert.Single(events.OfType<CorpsDissolvedEvent>());
        state.Commit(world);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FieldRulerUsesServicePlaceAndFallsBackToCurrentUnitWhenServiceIsLost(bool hasHome)
    {
        var state = World(); var world = state.BeginMutation(Rules().Abilities);
        world.SetSimulation(world.Simulation with { NextUnitId = 51 });
        world.AddUnit(new(50, 1, new(12, 2), 1000) { FormationId = 1, ServicePlaceId = hasHome ? 1 : null });
        world.Replace(world.Officers[11] with { SettlementId = null, UnitId = 50 });
        List<DomainEvent> events = []; SunMarriage.Apply(world, Rules(), events);
        Assert.Equal(hasHome ? 1 : null, world.Officers[22].SettlementId);
        Assert.Equal(hasHome ? null : 50, world.Officers[22].UnitId);
        Assert.Single(events.OfType<SunMarriageEvent>()); state.Commit(world);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DepartingUnitCommanderReelectsOrTerminatesWithoutMovingOtherPeople(bool otherMember)
    {
        var state = World(); var world = state.BeginMutation(Rules().Abilities);
        world.SetSimulation(world.Simulation with { NextUnitId = 51 });
        world.AddUnit(new(50, 2, new(17, 2), 1000) { FormationId = 1, ServicePlaceId = 4, CommanderId = 22,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front) });
        world.Replace(world.Officers[22] with { SettlementId = null, UnitId = 50 });
        if (otherMember) world.Replace(world.Officers[23] with { SettlementId = null, UnitId = 50 });
        List<DomainEvent> events = []; SunMarriage.Apply(world, Rules(), events);
        Assert.Equal(otherMember, world.Units.ContainsKey(50));
        if (otherMember) Assert.Equal(23, world.Units[50].CommanderId);
        else Assert.Single(events.OfType<UnitTerminatedEvent>());
        Assert.Equal(1, world.Officers[22].OwnerId); state.Commit(world);
    }

    [Fact]
    public void TransferCancelsRecruitmentTargetAndReturnsEnvoyWithoutChangingRecipientRuler()
    {
        var state = World(); var rules = Rules();
        var engine = new SimulationEngine(state, rules.Economy, rules.Personnel, rules.Treasures);
        Assert.True(engine.Execute(new("first"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 22)).Submission.Accepted);
        Guid task = Assert.Single(engine.Snapshot.Simulation.Tasks).Id;
        var world = state.BeginMutation(rules.Abilities); List<DomainEvent> events = [];
        SunMarriage.Apply(world, rules, events); state.Commit(world);
        Assert.Contains(events.OfType<TaskCancelledEvent>(), e => e.TaskId == task);
        Assert.DoesNotContain(world.Simulation.Tasks, t => t.Journey?.TargetOfficerId == 22 || t.OfficerId == 22);
        Assert.Equal(11, world.Forces[1].LeaderId); Assert.Equal(150, world.Officers[22].Loyalty);
        Assert.All(world.Simulation.Tasks, t => Assert.Equal(PersonnelReturns.Kind, t.Kind));
    }

    [Fact]
    public void FamilySelectionUsesFirstLivingForceAndBarbariansCountTowardMinimum()
    {
        var world = World().BeginMutation(Rules().Abilities); List<DomainEvent> events = [];
        world.Replace(world.Forces[3] with { IsBarbarian = true });
        world.Replace(world.Officers[31] with { BloodlineId = 900 });
        SunMarriage.Apply(world, Rules(), events);
        Assert.Equal(2, Assert.Single(events.OfType<SunMarriageEvent>()).DonorForceId);
    }

    [Fact]
    public void ExactDateBoundaryAndKnownRewardsStillConsumeWithoutDuplicateLearning()
    {
        var world = World().BeginMutation(Rules().Abilities); List<DomainEvent> events = [];
        foreach (int id in new[] { 11, 22 }) world.Replace(world.Officers[id] with { LearnedTactics = [1, 13] });
        ForceRelations.Set(world, 1, 2, 100, []);
        SunMarriage.Apply(world, Rules(Definition() with { LatestDate = new(190, 1, 1) }), events);
        Assert.Single(events.OfType<SunMarriageEvent>()); Assert.Empty(events.OfType<TacticLearnedEvent>());
        Assert.Equal(100, ForceRelations.Get(world, 1, 2).Value); Assert.Single(world.Simulation.ConsumedEventKeys);
    }
}
