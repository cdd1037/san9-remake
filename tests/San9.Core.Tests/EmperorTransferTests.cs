using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EmperorTransferTests
{
    private static SimulationRules Rules() => new()
    {
        Economy = new([], []) { ImperialCourt = new([99, 98], []) { TransferEvent = new(11, 15, 5) } },
        Politics = new(null, [new(8, 0, 0, 0), new(7, 1, 1000, 0)],
            [new CountryTitleRule(20, false, false, false, [], []) { ForcesEmperorDeposition = true },
             new CountryTitleRule(21, false, false, true, [], [])], ImmutableDictionary<int, int>.Empty)
    };
    private static OfficerState Person(int id, int? force, int city, OfficerStatus status, int righteousness = 15, int loyalty = 100) =>
        new(id, force, city, new(50, 50, 50, 50), false) { Status = status, Righteousness = righteousness,
            ForceLoyalties = force is int owner ? ImmutableDictionary<int, int>.Empty.Add(owner, loyalty) : ImmutableDictionary<int, int>.Empty };
    private static WorldState World(bool player = false) => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 100) { LeaderId = 1, BaseTrust = 100 },
         new(2, 100) { LeaderId = 2, BaseTrust = 100 },
         new(3, 100) { LeaderId = 3, BaseTrust = 100 }],
        Enumerable.Range(1, 4).Select(id => new SettlementState(id, id == 4 ? 1 : id, 100, 100, 100, 50, 300, 0, 100, 0, 100)
            { Kind = SettlementKind.City }),
        [Person(1, 1, 4, OfficerStatus.Ruler), Person(2, 2, 2, OfficerStatus.Ruler), Person(3, 3, 3, OfficerStatus.Ruler),
         Person(99, null, 1, OfficerStatus.Free), Person(98, null, 3, OfficerStatus.Free),
         Person(21, 2, 2, OfficerStatus.Serving, 11, 100), Person(22, 2, 2, OfficerStatus.Serving, 15, 253),
         Person(23, 2, 2, OfficerStatus.Serving, 10, 100), Person(24, 2, 2, OfficerStatus.Serving, 15, 2),
         Person(25, 2, 3, OfficerStatus.Captive, 15, 100)],
        controls: player ? [new("p2", 2, ControllerKind.Player)] : [],
        officeRequestsEnabled: false, naturalDeathsEnabled: false, freeOfficerMigrationEnabled: false,
        annualAppearancesEnabled: false, forceConsolidationEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 3, new(10000, 10000)) { IsRoot = true }]);
    private static WorldMutation Mutation(WorldState state, SimulationRules rules) => state.BeginMutation(rules.Abilities, rules.Economy);

    [Theory]
    [InlineData("2", 2)]
    [InlineData("2,3", 3)]
    [InlineData("2,1", 0)]
    [InlineData("2,3,2", 2)]
    public void MultipleTransfersRetainOnlyFirstOwnerAndResolveFinalOwner(string owners, int expected)
    {
        var rules = Rules(); var w = Mutation(World(), rules); List<DomainEvent> events = [];
        foreach (int owner in owners.Split(',').Select(int.Parse)) w.Replace(w.Settlements[1] with { OwnerId = owner });
        Assert.Equal(new EmperorTransferPending(1), w.Simulation.EmperorTransfer);
        EmperorTransfer.Begin(w, rules, events);
        Assert.Null(w.Simulation.EmperorTransfer);
        if (expected == 0) Assert.Empty(events.OfType<EmperorTransferResolvedEvent>());
        else Assert.Equal(expected, Assert.Single(events.OfType<EmperorTransferResolvedEvent>()).ForceId);
        EmperorTransfer.Begin(w, rules, events);
        Assert.Equal(expected == 0 ? 0 : 1, events.OfType<EmperorTransferResolvedEvent>().Count());
        Assert.Empty(w.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(EmperorTransferChoice.Support)]
    [InlineData(EmperorTransferChoice.Depose)]
    public void PlayerChoiceChangesOnlyEligibleLoyaltiesAndCourtHistory(EmperorTransferChoice choice)
    {
        var state = World(true); var rules = Rules(); var w = Mutation(state, rules); List<DomainEvent> events = [];
        var emperor = w.Officers[99];
        w.Replace(w.Settlements[1] with { OwnerId = 2 });
        EmperorTransfer.Begin(w, rules, events);
        Assert.IsType<EmperorTransferEventSession>(w.Simulation.PendingEvent);
        Assert.False(w.Simulation.CanPersist);
        Assert.Equal(100, w.Officers[21].LoyaltyTo(2));
        Assert.Equal(CommandRejection.InvalidDecision, EmperorTransfer.Respond(w, rules, new(Guid.NewGuid(), 2, 0, 99, 3, choice), events));
        Assert.Equal(CommandRejection.None, EmperorTransfer.Respond(w, rules, new(Guid.NewGuid(), 2, 0, 99, 1, choice), events));
        bool depose = choice == EmperorTransferChoice.Depose;
        Assert.Equal(depose ? 95 : 105, w.Officers[21].LoyaltyTo(2));
        Assert.Equal(depose ? 248 : 255, w.Officers[22].LoyaltyTo(2));
        Assert.Equal(depose ? 0 : 7, w.Officers[24].LoyaltyTo(2));
        Assert.All(new[] { 2, 23, 25 }, id => Assert.Equal(100, w.Officers[id].LoyaltyTo(2)));
        Assert.Equal(depose, w.Simulation.ImperialCourtEnded); Assert.Equal(depose, w.Forces[2].HasDeposedEmperor);
        Assert.False(w.Forces[1].HasDeposedEmperor); Assert.Equal(emperor, w.Officers[99]);
        Assert.Equal(100, w.Forces[2].BaseTrust); Assert.Equal(depose ? 100 : 200, w.Forces[2].Trust);
        Assert.Empty(w.Simulation.ConsumedEventKeys); Assert.Empty(w.Simulation.RandomStreams);
        Assert.Equal(CommandRejection.InvalidDecision, EmperorTransfer.Respond(w, rules, new(Guid.NewGuid(), 2, 1, 99, 1, choice), events));
        state.Commit(w);
    }

    [Theory]
    [InlineData("barbarian", true)]
    [InlineData("emperor-rank", true)]
    [InlineData("title", true)]
    [InlineData("investiture-only", false)]
    public void ForcedBranchesUseIndependentSemanticProperties(string branch, bool forced)
    {
        var rules = Rules(); var w = Mutation(World(true), rules); List<DomainEvent> events = [];
        w.Replace(w.Forces[2] with { IsBarbarian = branch == "barbarian", RankId = branch == "emperor-rank" ? 7 : null,
            CountryTitleId = branch == "title" ? 20 : branch == "investiture-only" ? 21 : null });
        w.Replace(w.Settlements[1] with { OwnerId = 2 }); EmperorTransfer.Begin(w, rules, events);
        Assert.Equal(forced, w.Simulation.ImperialCourtEnded);
        if (forced)
        {
            Assert.Null(w.Simulation.PendingEvent);
            Assert.True(Assert.Single(events.OfType<EmperorTransferResolvedEvent>()).Forced);
        }
        else Assert.IsType<EmperorTransferEventSession>(w.Simulation.PendingEvent);
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("disabled")]
    [InlineData("no-owner")]
    [InlineData("dead")]
    [InlineData("eliminated")]
    public void InvalidFinalCandidateClearsPendingWithoutRetry(string reason)
    {
        var rules = Rules(); var w = Mutation(World(), rules); List<DomainEvent> events = [];
        w.Replace(w.Settlements[1] with { OwnerId = 2 });
        if (reason == "ended") w.SetSimulation(w.Simulation with { ImperialCourtEnded = true });
        if (reason == "disabled") rules = new() { Economy = rules.Economy with { ImperialCourt = rules.Economy.ImperialCourt with { TransferEvent = null } } };
        if (reason == "no-owner") w.Replace(w.Settlements[1] with { OwnerId = null });
        if (reason == "dead") foreach (int id in new[] { 99, 98 }) w.Replace(w.Officers[id] with { Status = OfficerStatus.Dead });
        if (reason == "eliminated") w.Replace(w.Forces[2] with { IsEliminated = true });
        EmperorTransfer.Begin(w, rules, events);
        Assert.Null(w.Simulation.EmperorTransfer); Assert.Empty(events.OfType<EmperorTransferResolvedEvent>());
    }

    [Fact]
    public void CurrentEmperorAndCityAreResolvedAgainAtCleanup()
    {
        var rules = Rules(); var w = Mutation(World(), rules); List<DomainEvent> events = [];
        w.Replace(w.Settlements[1] with { OwnerId = 2 });
        w.Replace(w.Officers[99] with { Status = OfficerStatus.Dead });
        EmperorTransfer.Begin(w, rules, events);
        var result = Assert.Single(events.OfType<EmperorTransferResolvedEvent>());
        Assert.Equal(98, result.EmperorId); Assert.Equal(3, result.CityId); Assert.Equal(3, result.ForceId);
    }

    [Fact]
    public void UnownedOriginIsDistinctFromNoTransferAndUnrelatedChangesDoNotTrigger()
    {
        var state = World(); var setup = state.BeginMutation(); setup.Replace(setup.Settlements[1] with { OwnerId = null }); state.Commit(setup);
        var rules = Rules(); var w = Mutation(state, rules); List<DomainEvent> events = [];
        w.Replace(w.Settlements[1] with { Soldiers = 80 });
        w.Replace(w.Settlements[4] with { OwnerId = 2 }); Assert.Null(w.Simulation.EmperorTransfer);
        w.Replace(w.Settlements[1] with { OwnerId = 2 }); Assert.Equal(new EmperorTransferPending(null), w.Simulation.EmperorTransfer);
        EmperorTransfer.Begin(w, rules, events); Assert.Equal(2, Assert.Single(events.OfType<EmperorTransferResolvedEvent>()).ForceId);
    }

    [Fact]
    public void SurrenderUsesSharedOwnershipObservationAndRetainsEliminatedFormerOwner()
    {
        var state = World(true); var rules = Rules(); var w = Mutation(state, rules); List<DomainEvent> events = [];
        var cause = new PersistentTask(Guid.NewGuid(), SurrenderDiplomacy.Kind, "p2", 2, 2, 2, w.Clock, w.Clock, 0, 0);
        SurrenderMerge.Apply(w, cause, 1, rules, events);
        Assert.True(w.Forces[1].IsEliminated);
        Assert.Equal(2, w.Settlements[1].OwnerId);
        Assert.Equal(new EmperorTransferPending(1), w.Simulation.EmperorTransfer);
        Assert.Empty(events.OfType<EmperorTransferResolvedEvent>());
        EmperorTransfer.Begin(w, rules, events);
        Assert.Equal(new EmperorTransferEventSession(2, 99, 1), w.Simulation.PendingEvent);
        Assert.Equal(CommandRejection.None, EmperorTransfer.Respond(w, rules,
            new(Guid.NewGuid(), 2, 0, 99, 1, EmperorTransferChoice.Support), events));
        state.Commit(w);
    }

    [Fact]
    public void TerminalPriorityStopsBeforePendingTransferAndEffects()
    {
        var rules = Rules(); var w = Mutation(World(true), rules); List<DomainEvent> events = [];
        w.Replace(w.Settlements[1] with { OwnerId = 2 });
        w.SetSimulation(w.Simulation with { TerminalReason = "player-defeated" });
        new ResolutionPipeline(rules).Prepare(w, events);
        Assert.Equal(SimulationBoundary.Terminal, w.Simulation.Boundary);
        Assert.Null(w.Simulation.PendingEvent);
        Assert.Empty(events.OfType<EmperorTransferResolvedEvent>());
        Assert.Equal(100, w.Officers[21].LoyaltyTo(2));
    }
}
