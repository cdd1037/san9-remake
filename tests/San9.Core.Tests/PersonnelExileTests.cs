using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelExileTests
{
    private static readonly EconomyRulebook Economy = new([new(1, 1)], []);
    private static PersonnelRulebook Geography(bool connected = true) => new([new(1, new(0, 0)), new(2, new(300, 0)), new(20, new(350, 0))],
        new Dictionary<int, int> { [1] = 1, [2] = 2, [20] = 2 }, connected ? [new(1, 2)] : []);
    private static SimulationEngine Engine(bool connected = true, bool barbarianDestination = false)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 0) { LeaderId = 1, AdviserId = 11 }, new(2, 0) { IsBarbarian = barbarianDestination }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(11, 1, 1, new(50, 70, 50, 50), false)
             { Status = OfficerStatus.Strategist, OfficeId = 1, LearnedTactics = [2], RewardedThisXun = true }.WithLoyalty(1, 90).WithLoyalty(2, 177)],
            treasures: [new(10, 11, TreasureDiscovery.Discovered, null)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(1000, 1000)) { IsRoot = true }]);
        return new(world, Economy, Geography(connected), new([new(10, new(5, 0, 0, 0), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(4, 0, 0, 0) }]));
    }

    private static CommandExecutionResult Exile(SimulationEngine engine, int person = 11) => engine.Execute(new("player"),
        new ExileOfficerCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, person));

    [Fact]
    public void ExileClearsAppointmentsButKeepsTreasureAndUnrelatedLoyalty()
    {
        var engine = Engine();
        uint money = engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money;
        var result = Exile(engine);
        Assert.True(result.Submission.Accepted);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Free, person.Status);
        Assert.Null(person.OwnerId);
        Assert.Contains(person.SettlementId ?? person.RegionId, new int?[] { 2, 20 });
        Assert.NotEqual(person.SettlementId is null, person.RegionId is null);
        Assert.Null(engine.Snapshot.Forces.Single(f => f.Id == 1).AdviserId);
        Assert.Null(person.OfficeId);
        Assert.Equal(new OfficerAbilities(55, 70, 50, 50), engine.GetAbilities(engine.Snapshot)[person.Id]);
        Assert.Equal(11, engine.Snapshot.Treasures.Single().HolderId);
        Assert.Equal(45, person.LoyaltyTo(1));
        Assert.Equal(177, person.LoyaltyTo(2));
        Assert.Equal(190, person.StatusStartYear);
        Assert.Equal(new[] { 2 }, person.LearnedTactics.ToArray());
        Assert.True(person.Acted);
        Assert.Equal(money, engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        var random = new StableRandom(StableRandom.DeriveSeed(190, "personnel-exile-placement"));
        random.NextUInt64();
        Assert.Equal(random.State, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "personnel-exile-placement").State);
        Assert.Single(result.Events.OfType<OfficerExiledEvent>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NoEligibleDestinationRejectsBeforeRandomOrLoyaltyChanges(bool connected, bool barbarian)
    {
        var engine = Engine(connected, barbarian);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.NoDestination, Exile(engine).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(1, CommandRejection.PersonUnavailable)]
    [InlineData(999, CommandRejection.UnknownPerson)]
    public void InvalidSubjectDoesNotMutate(int person, CommandRejection rejection)
    {
        var engine = Engine();
        string before = engine.Snapshot.StateHash;
        Assert.Equal(rejection, Exile(engine, person).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void ValidationAndRestorePreservePlacementRandomness()
    {
        var engine = Engine();
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), Economy, Geography(), new([new(10, new(5, 0, 0, 0), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(4, 0, 0, 0) }]));
        var command = new ExileOfficerCommand(Guid.NewGuid(), 1, 0, 11);
        string before = engine.Snapshot.StateHash;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.Equal(CanonicalJson.Text(engine.Execute(new("player"), command).Events),
            CanonicalJson.Text(restored.Execute(new("player"), command).Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.PersonUnavailable, Exile(engine).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void ExileCancelsRivalRecruitmentWithoutChangingTheRecruitersAllegiance()
    {
        var state = Engine().Snapshot;
        state = state with
        {
            Corps = state.Corps.Select(c => c.IsRoot && c.OwnerId == 2 ? c with { LeaderId = 22 } : c).ToArray(),
            Forces = state.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 22 } : f).ToArray(),
            Officers = [.. state.Officers, new(22, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler }],
            Relations = [new(1, 2, 50)],
            Simulation = state.Simulation with { Controls = [new("enemy", 2, ControllerKind.Player), new("player", 1, ControllerKind.Player)] }
        };
        var engine = new SimulationEngine(WorldState.Restore(state), Economy, Geography(), new([new(10, new(5, 0, 0, 0), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(4, 0, 0, 0) }]));
        Assert.True(engine.Execute(new("enemy"), new RecruitOfficerCommand(Guid.NewGuid(), 2, 0, 22, 11)).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        engine.EndControl(new("enemy"));
        var result = Exile(engine);
        Assert.True(result.Submission.Accepted);
        Assert.Equal("personnel-return", Assert.Single(engine.Snapshot.Simulation.Tasks).Kind);
        Assert.Equal(task.Id, Assert.Single(result.Events.OfType<TaskCancelledEvent>()).TaskId);
        var recruiter = engine.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal(2, recruiter.OwnerId);
        Assert.Equal(2, recruiter.SettlementId);
        Assert.True(recruiter.Acted);
        engine.EndControl(new("player"));
        Assert.Single(engine.AdvanceXun().Events.OfType<PersonnelReturnedEvent>());
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }
}
