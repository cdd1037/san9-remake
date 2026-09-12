using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelMissionTests
{
    private static readonly PersonnelRulebook Places = new([new(1, MapCoordinates.Center(new(0, 0))), new(2, MapCoordinates.Center(new(90, 0))), new(3, MapCoordinates.Center(new(0, 1))), new(30, MapCoordinates.Center(new(30, 30)))]);
    private static WorldState World(ulong seed = 190) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 10000, 10000, 1000) { LeaderId = 1, BaseTrust = 1000 }, new(2, 10000, 10000, 0) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(3, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Affinity = 50 },
         new(11, 1, 1, new(50, 50, 100, 50), false), new(12, 1, 1, new(50, 50, 100, 50), false),
         new(13, 1, 3, new(50, 50, 100, 50), false),
         new(20, 2, 2, new(50, 50, 50, 50), false) { ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 90), Righteousness = 5 },
         new(21, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered, RegionId = 30, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 0) }],
        relations: [new(1, 2, 80)], controls: [new("player", 1, ControllerKind.Player)]);
    private static SimulationEngine Engine(WorldState world) => new(world, EconomyRulebook.Empty, Places);
    private static CommandExecutionResult Recruit(SimulationEngine engine, int actor = 11, int target = 20) =>
        engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, actor, target));
    private static IReadOnlyList<DomainEvent> Advance(SimulationEngine engine)
    {
        engine.EndControl(new("player"));
        return engine.AdvanceXun().Events;
    }

    [Fact]
    public void RecruitmentWaitsAndRestoresSubmissionRollThenChangesAffiliation()
    {
        var engine = Engine(World());
        Assert.True(Recruit(engine).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(new GameClock(190, 1, 16), task.Due);
        Assert.Equal(1, task.OriginSettlementId);
        Assert.Equal(2, task.Journey!.DestinationPlaceId);
        Advance(engine);
        Assert.Equal(2, engine.Snapshot.Officers.Single(p => p.Id == 20).OwnerId);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        Assert.Equal(task.Journey.SuccessRoll, Assert.Single(restored.Snapshot.Simulation.Tasks).Journey!.SuccessRoll);
        var events = Advance(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.True(Assert.Single(events.OfType<RecruitmentResolvedEvent>()).Success);
        var target = restored.Snapshot.Officers.Single(p => p.Id == 20);
        Assert.Equal(1, target.OwnerId);
        Assert.Equal(1, target.SettlementId);
        Assert.Equal(45, target.LoyaltyTo(2));
        Assert.Equal(100, target.LoyaltyTo(1));
        Assert.Empty(restored.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void ArrivalRecomputesThresholdAndFailureKeepsAffiliation()
    {
        var world = World();
        var engine = Engine(world);
        Assert.True(Recruit(engine).Submission.Accepted);
        var change = world.BeginMutation();
        change.Replace(change.Officers[20].WithLoyalty(2, 100));
        world.Commit(change);
        Advance(engine);
        var events = Advance(engine);
        var result = Assert.Single(events.OfType<RecruitmentResolvedEvent>());
        Assert.False(result.Success);
        Assert.Equal(0, result.Threshold);
        var target = engine.Snapshot.Officers.Single(p => p.Id == 20);
        Assert.Equal(2, target.OwnerId);
        Assert.Equal(2, target.SettlementId);
        Assert.Equal(100, target.LoyaltyTo(2));
        Assert.DoesNotContain(events, e => e is OfficerAbilityGrownEvent);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, r => r.Name == "personnel-politics-growth");
    }

    [Fact]
    public void RecruitingAnAdviserClearsTheOldForceReference()
    {
        var world = World();
        var change = world.BeginMutation();
        change.Replace(change.Officers[20] with { Status = OfficerStatus.Strategist });
        change.Replace(change.Forces[2] with { AdviserId = 20 });
        world.Commit(change);
        var engine = Engine(world);
        Assert.True(Recruit(engine).Submission.Accepted);
        Advance(engine);
        Advance(engine);
        Assert.Null(engine.Snapshot.Forces.Single(f => f.Id == 2).AdviserId);
        Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 20).Status);
        Assert.Equal(engine.Snapshot.StateHash, Engine(WorldState.Restore(engine.Snapshot)).Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    public void NonHostingOrDestroyedDestinationsRejectAllMoveParticipants(bool canHost, int durability)
    {
        var world = World();
        var change = world.BeginMutation();
        change.Replace(change.Settlements[3] with { CanHostPersons = canHost, Durability = durability });
        world.Commit(change);
        var engine = Engine(world);
        string before = engine.Snapshot.StateHash;
        var result = engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [11, 12], 3));
        Assert.False(result.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void FirstRecruitmentCancelsCompetingRecruitmentExactlyOnce()
    {
        var engine = Engine(World());
        Assert.True(Recruit(engine, 11).Submission.Accepted);
        Assert.True(Recruit(engine, 12).Submission.Accepted);
        Advance(engine);
        var events = Advance(engine);
        Assert.Single(events.OfType<RecruitmentResolvedEvent>());
        Assert.Single(events.OfType<TaskCompletedEvent>());
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void MultiOriginMoveIsAtomicAndUsesOneTaskPerOfficer()
    {
        var engine = Engine(World());
        var result = engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [13, 11], 1));
        Assert.False(result.Submission.Accepted); // One participant is already at the destination.
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        var world = WorldState.Restore(engine.Snapshot);
        var change = world.BeginMutation();
        change.Replace(change.Settlements[2] with { OwnerId = 1 });
        change.Replace(change.Officers[2] with { SettlementId = 1, OwnerId = 1 });
        change.Replace(change.Forces[2] with { LeaderId = null });
        change.Replace(change.Officers[20] with { SettlementId = 1, OwnerId = 1 });
        world.Commit(change);
        engine = Engine(world);
        Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [13, 11], 2)).Submission.Accepted);
        Assert.Equal(2, engine.Snapshot.Simulation.Tasks.Count());
        Assert.Single(engine.Snapshot.Simulation.Tasks.Select(t => t.Sequence).Distinct());
        Assert.Equal(new[] { 1, 3 }, engine.Snapshot.Simulation.Tasks.Select(t => t.OriginSettlementId));
        Advance(engine);
        var restored = Engine(WorldState.Restore(engine.Snapshot));
        var events = Advance(restored);
        Assert.Equal(2, events.OfType<OfficerMovedEvent>().Count());
        Assert.All(restored.Snapshot.Officers.Where(p => p.Id is 11 or 13), p => Assert.Equal(2, p.SettlementId));
    }

    [Fact]
    public void DestinationLossCancelsMovementAndAffiliationChangeCancelsTravel()
    {
        var world = World();
        var engine = Engine(world);
        Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [11], 3)).Submission.Accepted);
        var change = world.BeginMutation();
        change.Replace(change.Officers[13] with { SettlementId = 1 });
        change.Replace(change.Settlements[3] with { OwnerId = 2 });
        world.Commit(change);
        Assert.Single(Advance(engine).OfType<TaskCancelledEvent>());
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        Assert.True(Recruit(engine).Submission.Accepted);
        change = world.BeginMutation();
        List<DomainEvent> events = [];
        PersonnelTransitions.Recruit(change, 11, 2, 2, events);
        world.Commit(change);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(2, engine.Snapshot.Officers.Single(p => p.Id == 11).OwnerId);
    }

    [Fact]
    public void SearchDiscoversOnlyAtDestinationAndCanBeFollowedByRecruitment()
    {
        var engine = Engine(World());
        bool found = false;
        for (int attempt = 0; attempt < 10 && !found; attempt++)
        {
            Assert.True(engine.Execute(new("player"), new SearchPersonsCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, 11, 30)).Submission.Accepted);
            Advance(engine);
            var events = Advance(engine);
            found = Assert.Single(events.OfType<PersonSearchResolvedEvent>()).DiscoveredOfficerId == 21;
        }
        Assert.True(found);
        var target = engine.Snapshot.Officers.Single(p => p.Id == 21);
        Assert.Equal(OfficerStatus.Free, target.Status);
        Assert.Null(target.OwnerId);
        Assert.Null(target.SettlementId);
        Assert.Equal(30, target.RegionId);
        Assert.Equal(49, target.LoyaltyTo(1));
        for (int attempt = 0; attempt < 10 && engine.Snapshot.Officers.Single(p => p.Id == 21).OwnerId is null; attempt++)
        {
            Assert.True(Recruit(engine, target: 21).Submission.Accepted);
            Advance(engine);
            Advance(engine);
        }
        target = engine.Snapshot.Officers.Single(p => p.Id == 21);
        Assert.Equal(1, target.OwnerId);
        Assert.Null(target.RegionId);
        Assert.Equal(1, target.SettlementId);
    }

    [Fact]
    public void InvalidMovementAndBusyRecruitmentDoNotConsumeRandomOrSequence()
    {
        var engine = Engine(World());
        string before = engine.Snapshot.StateHash;
        var unavailable = engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [11], 30));
        Assert.False(unavailable.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(Recruit(engine).Submission.Accepted);
        before = engine.Snapshot.StateHash;
        Assert.False(Recruit(engine).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void SearchIncreasesLoyaltyBeforeTestingImmediateRecruitment()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[21].WithLoyalty(1, 40));
        world.Commit(mutation);
        var engine = Engine(world);
        List<DomainEvent> events = [];
        for (int attempt = 0; attempt < 10 && engine.Snapshot.Officers.Single(p => p.Id == 21).OwnerId is null; attempt++)
        {
            Assert.True(engine.Execute(new("player"), new SearchPersonsCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, 11, 30)).Submission.Accepted);
            events.AddRange(Advance(engine));
            events.AddRange(Advance(engine));
        }
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 21).OwnerId);
        Assert.Contains(events, e => e is OfficerLoyaltyChangedEvent { Previous: 40, Current: 60 });
        Assert.Contains(events, e => e is PersonSearchResolvedEvent { Recruited: true });
    }

    [Fact]
    public void SearchRejectsSamePlaceAndDestroyedOriginWithoutChangingState()
    {
        var world = World();
        var engine = Engine(world);
        string before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), new SearchPersonsCommand(Guid.NewGuid(), 1, 0, 11, 1)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { Durability = 0 });
        world.Commit(mutation);
        before = engine.Snapshot.StateHash;
        Assert.False(Recruit(engine).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void ZeroDistanceRecruitmentRestoresAndResolvesAtFirstDailyCommandStage()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[20] with { OwnerId = null, Status = OfficerStatus.Free, SettlementId = 1 });
        world.Commit(mutation);
        var engine = Engine(world);
        Assert.True(Recruit(engine).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(task.Started, task.Due);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var events = Advance(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
        Assert.Equal(task.Started, Assert.Single(events.OfType<RecruitmentResolvedEvent>()).Clock);
    }
}
