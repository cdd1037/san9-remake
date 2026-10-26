using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PoliticalEventsTests
{
    [Fact]
    public void NewIndependentForceDoesNotInvalidateSubsequentCountryTitleChoice()
    {
        var state = World(5, true);
        var world = Due(state);
        var rules = Rules();
        List<DomainEvent> events = [];
        ForceCreation.Found(world, 4, world.Officers[12], [world.Officers[12]], ForceCreationKind.CityIndependence, rules, events);
        world.Replace(world.Forces[1] with { BaseTrust = 1000, Trust = 1000 });
        PoliticalEvents.Begin(world, rules, events);
        Assert.IsType<CountryTitleEventSession>(world.Simulation.PendingEvent);
        state.Commit(world);
        var engine = new SimulationEngine(state, rules);
        var answer = engine.Execute(new("player"), new ChooseCountryTitleCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, 41));
        Assert.True(answer.Submission.Accepted);
        Assert.Equal(SimulationBoundary.Stable, answer.Snapshot.Simulation.Boundary);
        Assert.Equal(41, answer.Snapshot.Forces.Single(f => f.Id == 1).CountryTitleId);
        Assert.Equal(3, answer.Snapshot.Simulation.Controls.Length);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    private static SettlementState City(int id, int owner) => new(id, owner, 1000, 100, 100, 50, 500, 100, 1000, 100, 1000)
        { Kind = SettlementKind.City, ConsumesFood = false };
    private static WorldState World(int rank = 700, bool ended = false) => WorldState.Create(new(190, 1, 1), 17,
        [new(1, 1000) { LeaderId = 11, RankId = rank, BaseTrust = ended ? 1000 : 900 },
         new(2, 500) { LeaderId = 21, RankId = 700, BaseTrust = 300 }],
        [City(1, 1), City(2, 2), City(3, 2), City(4, 1)],
        [new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(12, 1, 1, new(50, 50, 50, 50), false).WithLoyalty(1, 254),
         new OfficerState(13, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive }.WithLoyalty(1, 150),
         new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(22, 2, 3, new(50, 50, 50, 50), false),
         new(31, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free }],
        controls: [new("player", 1, ControllerKind.Player), new("ai", 2, ControllerKind.Automatic)], imperialCourtEnded: ended, corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);
    private static PoliticalRulebook Politics(CountryTitleRule[]? titles = null) => new(new(1000, new("court-ended")),
        [new(700, 0, 0, 0), new(300, 10, 100, 4), new(5, 20, 400, 5), new(99, 30, 1000, 6)],
        titles ?? [new(41, true, true, false, [], []), new(91, true, true, false, [], []), new(111, false, true, true, [], [])],
        ImmutableDictionary<int, int>.Empty);
    private static SimulationRules Rules(PoliticalRulebook? politics = null) => new()
    {
        Politics = politics ?? Politics(),
        Personnel = new(Enumerable.Range(1, 4).Select(id => new PersonPlace(id, new(id, id))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4 }, [],
            new Dictionary<int, int> { [1] = 10, [2] = 10, [3] = 20, [4] = 30 }),
        Economy = EconomyRulebook.Empty with { ImperialCourt = new([31], []) }
    };
    private static WorldMutation Due(WorldState state)
    {
        var world = state.BeginMutation(economy: Rules().Economy); world.RebuildForceTrust(); world.SetClock(new(190, 4, 1));
        world.SetSimulation(world.Simulation with { ActiveControl = world.Simulation.Controls.Length }); return world;
    }

    [Theory]
    [InlineData(false, 79)]
    [InlineData(true, 84)]
    public void CaptiveLoyaltyAcrossQuarterOnlyRisesWhenOriginalForceIsPromoted(bool promoted, int expected)
    {
        var state = World(300);
        var world = state.BeginMutation();
        world.SetClock(new(190, 3, 21));
        world.Replace(world.Forces[1] with { BaseTrust = promoted ? 900 : 0 });
        world.Replace(world.Officers[13].WithLoyalty(1, 80).WithLoyalty(2, 49) with { Affinity = 50 });
        world.SetSimulation(world.Simulation with
        {
            Boundary = SimulationBoundary.Stable, ActiveControl = world.Simulation.Controls.Length,
            RandomStreams = [new("personnel-prisoner-escape", TestSeeds.StateForRoll(99))]
        });
        state.Commit(world);
        var engine = new SimulationEngine(state, Rules());
        var result = engine.AdvanceXun();
        Assert.Equal(new GameClock(190, 4, 1), result.Snapshot.Clock);
        var captive = result.Snapshot.Officers.Single(p => p.Id == 13);
        Assert.Equal(OfficerStatus.Captive, captive.Status);
        Assert.Equal(1, captive.OwnerId);
        Assert.Equal(2, captive.SettlementId);
        Assert.Equal(expected, captive.Loyalty);
        Assert.Equal(50, captive.LoyaltyTo(2));
        var changes = result.Events.OfType<OfficerLoyaltyChangedEvent>().Where(e => e.OfficerId == 13).ToArray();
        Assert.Single(changes, e => e.Reason == "prisoner-xun");
        Assert.DoesNotContain(changes, e => e.ForceId == 1 && e.Reason == "quarterly-affiliation-interest");
        Assert.Equal(promoted ? 1 : 0, changes.Count(e => e.Reason == "peerage"));
        Assert.Equal(promoted, result.Events.OfType<PeerageChangedEvent>().Any(e => e.ForceId == 1));
    }

    [Fact]
    public void InvestitureRequiresWholeNonemptyProvinceAndPromotesAllEligibleForcesOnlyOnce()
    {
        var state = World(); var world = Due(state);
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(new[] { 1, 2 }, events.OfType<PeerageChangedEvent>().Select(e => e.ForceId));
        Assert.All(world.Forces.Values, f => Assert.Equal(300, f.RankId));
        Assert.Equal(910, world.Forces[1].BaseTrust); Assert.Equal(1000, world.Forces[1].Trust);
        Assert.Equal(255, world.Officers[12].Loyalty); Assert.Equal(154, world.Officers[13].Loyalty);
        Assert.Empty(world.Officers[11].ForceLoyalties); state.Commit(world);
        world = Due(World()); world.Replace(world.Settlements[4] with { OwnerId = 2 });
        events.Clear(); PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(2, Assert.Single(events.OfType<PeerageChangedEvent>()).ForceId);
        Assert.Equal(700, world.Forces[1].RankId);
    }

    [Theory]
    [InlineData(399, false)]
    [InlineData(400, true)]
    public void HigherRankUsesNextThresholdRatherThanCurrentRankOrIdentity(int trust, bool promoted)
    {
        var world = Due(World(300)); world.Replace(world.Forces[1] with { BaseTrust = trust - 100 });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(promoted ? 5 : 300, world.Forces[1].RankId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void InvestitureExclusionsDoNotApplyAfterCourtEnds(bool ended, bool promoted)
    {
        var world = Due(World(300, ended));
        world.Replace(world.Forces[1] with { CountryTitleId = 111 });
        world.Replace(world.Forces[2] with { IsBarbarian = true });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(promoted ? 5 : 300, world.Forces[1].RankId);
        Assert.Equal(promoted ? 300 : 700, world.Forces[2].RankId);
    }

    [Fact]
    public void MissingEmperorDoesNotEndCourtOrPermitKingPromotion()
    {
        var world = Due(World(5)); world.Replace(world.Officers[31] with { Status = OfficerStatus.Dead, SettlementId = null });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(5, world.Forces[1].RankId); Assert.False(world.Simulation.ImperialCourtEnded);
        Assert.Equal(PeerageChangeKind.Investiture, Assert.Single(events.OfType<PeerageChangedEvent>()).Kind);
    }

    [Theory]
    [InlineData(false, 1000)]
    [InlineData(true, 1000)]
    public void ExistingTitleSkipsChoiceAndProclamationStopsLaterPromotion(bool ended, int expectedBase)
    {
        var state = World(5, ended); var world = Due(state); world.Replace(world.Forces[1] with { CountryTitleId = 91 });
        // A captive ruler remains the leader but is not status Ruler, so receives the bonus.
        world.Replace(world.Officers[11].WithLoyalty(1, 120) with { Status = OfficerStatus.Captive, SettlementId = 2 });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Single(events.OfType<PeerageChangedEvent>()); Assert.Null(world.Simulation.PendingEvent);
        Assert.Equal(99, world.Forces[1].RankId); Assert.Equal(91, world.Forces[1].CountryTitleId);
        Assert.Equal(700, world.Forces[2].RankId); Assert.Equal(expectedBase, world.Forces[1].BaseTrust);
        Assert.Equal(126, world.Officers[11].Loyalty); Assert.True(world.Simulation.ImperialCourtEnded);
        Assert.Equal(!ended, world.Simulation.ConsumedEventKeys.Contains(new("court-ended"))); state.Commit(world);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AutomaticTitleUsesPublicChoiceRegardlessOfPlayerCount(int players)
    {
        var world = Due(World(5, true));
        world.SetSimulation(world.Simulation with { Controls = players == 0 ? [new("ai", 1, ControllerKind.Automatic), new("ai2", 2, ControllerKind.Automatic)] :
            [new("ai", 1, ControllerKind.Automatic), new("player", 2, ControllerKind.Player)] });
        var rules = Rules(Politics([new(41, true, true, false, [11], [])]));
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, rules, events);
        Assert.IsType<CountryTitleEventSession>(world.Simulation.PendingEvent);
        Assert.Equal(CommandRejection.None, PoliticalEvents.Respond(world, rules, new(Guid.NewGuid(), 1, 0, 41), events));
        Assert.Equal(41, world.Forces[1].CountryTitleId); Assert.Equal(99, world.Forces[1].RankId);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPlayerMenuFallsBackOrProclaimsWithoutInventingTitle(bool automatic)
    {
        var state = World(5, true); var world = Due(state);
        var rules = Rules(Politics([new(3333, false, automatic, false, [11], [])]));
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, rules, events);
        Assert.Null(world.Simulation.PendingEvent); Assert.Equal(99, world.Forces[1].RankId);
        Assert.Equal(automatic ? 3333 : null, world.Forces[1].CountryTitleId); state.Commit(world);
    }

    [Fact]
    public void OnlyFirstProclamationRunsAndSecondEligibleForceRetriesNextQuarter()
    {
        var state = World(5, true); var world = Due(state);
        world.Replace(world.Forces[1] with { CountryTitleId = 91 });
        world.Replace(world.Forces[2] with { CountryTitleId = 41, RankId = 5, Trust = 1000, BaseTrust = 1000 });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(1, Assert.Single(events.OfType<PeerageChangedEvent>()).ForceId);
        Assert.Equal(5, world.Forces[2].RankId); state.Commit(world);
        world = state.BeginMutation(economy: Rules().Economy); world.RebuildForceTrust(); world.SetClock(new(190, 7, 1)); events.Clear();
        PoliticalEvents.Begin(world, Rules(), events);
        Assert.Equal(2, Assert.Single(events.OfType<PeerageChangedEvent>()).ForceId);
        Assert.Equal(99, world.Forces[2].RankId); state.Commit(world);
    }

    [Fact]
    public void PlayerCorpsDoNotChangeTheAutomaticTitleChoice()
    {
        var state = World(5, true); var world = Due(state);
        world.SetSimulation(world.Simulation with { Controls = [new("ai", 1, ControllerKind.Automatic), new("player", 2, ControllerKind.Player)] });
        world.AddCorps(new(50, 2, 22, new(0, 0)) { ControlMode = CorpsControlMode.Direct });
        world.Replace(world.Settlements[3] with { CorpsId = 50 });
        List<DomainEvent> events = []; world.MaintainLeadership(events);
        PoliticalEvents.Begin(world, Rules(), events);
        Assert.IsType<CountryTitleEventSession>(world.Simulation.PendingEvent);
        Assert.Equal(5, world.Forces[1].RankId); Assert.Null(world.Forces[1].CountryTitleId);
        Assert.Empty(world.Simulation.RandomStreams); state.Commit(world);
    }

    [Fact]
    public void FieldRulerAutomaticPreferenceUsesServiceCityNotFieldPosition()
    {
        var state = World(5, true); var world = Due(state);
        world.SetSimulation(world.Simulation with { NextUnitId = 101 });
        world.AddUnit(new(100, 1, new(999, 999), 1000) { FormationId = 9, ServicePlaceId = 4, CommanderId = 11,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) });
        world.Replace(world.Officers[11] with { SettlementId = null, UnitId = 100, IsCommander = false });
        var original = Politics();
        var politics = new PoliticalRulebook(original.Events, original.Ranks.Values,
            [new(41, false, true, false, [], []), new(91, false, true, false, [], [])],
            new Dictionary<int, int> { [20] = 41, [30] = 91 });
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(politics), events);
        Assert.Equal(91, world.Forces[1].CountryTitleId); Assert.Empty(world.Simulation.RandomStreams);
        state.Commit(world);
    }

    [Fact]
    public void RestoredPendingEventRejectsStaleCandidateCatalogAndRankEligibility()
    {
        var state = World(5); var world = Due(state); List<DomainEvent> events = [];
        PoliticalEvents.Begin(world, Rules(), events); state.Commit(world);
        var snapshot = state.CreateSnapshot();
        var pending = (CountryTitleEventSession)snapshot.Simulation.PendingEvent!;
        var staleTrust = snapshot with { Forces = snapshot.Forces.Select(f => f with { Trust = 1 }).ToArray() };
        var restored = new SimulationEngine(WorldState.Restore(staleTrust), Rules());
        StateAssert.Equal(snapshot, restored.Snapshot);
        var invalidInput = snapshot with { Forces = snapshot.Forces.Select(f => f with { BaseTrust = 0 }).ToArray() };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(invalidInput), Rules()));
        var changed = snapshot with { Simulation = snapshot.Simulation with { PendingEvent = pending with { CandidateIds = [123456] } } };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(changed), Rules()));
        changed = snapshot with { Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with { RankId = 700 } : f).ToArray() };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(WorldState.Restore(changed), Rules()));
    }

    [Fact]
    public void MissingProvinceGroupsDoNotMakeAnEmptyProvinceControlled()
    {
        var world = Due(World());
        var empty = new SimulationRules { Politics = Politics(), Personnel = PersonnelRulebook.Empty };
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, empty, events);
        Assert.Empty(events); Assert.All(world.Forces.Values, f => Assert.Equal(700, f.RankId));
    }

    [Fact]
    public void ThirtyDayGateAndDisabledCatalogHaveNoEffectsOrRandomConsumption()
    {
        var world = Due(World(5)); world.SetClock(new(190, 1, 30));
        List<DomainEvent> events = []; PoliticalEvents.Begin(world, Rules(), events);
        Assert.Empty(events); Assert.Null(world.Simulation.PendingEvent);
        world.SetClock(new(190, 4, 1)); PoliticalEvents.Begin(world, Rules(PoliticalRulebook.Empty), events);
        Assert.Empty(events); Assert.Empty(world.Simulation.RandomStreams);
    }
}
