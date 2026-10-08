using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelMissionTests
{
    [Fact]
    public void AccurateRecruitAdviceUsesTheSubmittedRollAndLiveThresholdWithoutMutatingWorld()
    {
        var world = World(); var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { AdviserId = 12 });
        mutation.Replace(mutation.Officers[12] with { Status = OfficerStatus.Strategist });
        world.Commit(mutation); var engine = Engine(world);
        string before = engine.Snapshot.StateHash;
        var preview = engine.GetPersonnelJourney(1,11,20,PersonnelJourneyKind.Recruit);
        for (int i = 0; i < 30; i++) Assert.Equal(preview,engine.GetPersonnelJourney(1,11,20,PersonnelJourneyKind.Recruit));
        Assert.Equal(before,engine.Snapshot.StateHash);
        Assert.True(preview.Available);
        Assert.Equal(15,preview.Days);
        Assert.True(Recruit(engine).Submission.Accepted);
        RecruitmentResolvedEvent? outcome = null;
        for (int i = 0; i < 3 && outcome is null; i++) outcome = Advance(engine).OfType<RecruitmentResolvedEvent>().SingleOrDefault();
        Assert.NotNull(outcome);
        Assert.Equal(outcome.Success,preview.Advice.Opinion == AdviserOpinion.Favorable);
        Assert.Equal(outcome.Roll < outcome.Threshold,outcome.Success);
    }
    [Fact]
    public void SearchPreviewRejectsUnavailableActorsAndIncludesTheDiscoveryGate()
    {
        var world = World(); var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[1] with { AdviserId = 12 });
        mutation.Replace(mutation.Officers[12] with { Status = OfficerStatus.Strategist });
        mutation.Replace(mutation.Officers[11] with { BaseAbilities = new(50,50,100,200) });
        world.Commit(mutation); var engine = Engine(world);
        Assert.Contains(30,engine.GetSearchPlaces(1));
        Assert.DoesNotContain(2,engine.GetSearchPlaces(1));
        Assert.Equal(AdviserOpinion.Favorable,engine.GetPersonnelJourney(1,11,30,PersonnelJourneyKind.Search).Advice.Opinion);
        Assert.Equal(AdviserOpinion.Unfavorable,engine.GetPersonnelJourney(1,11,1,PersonnelJourneyKind.Search).Advice.Opinion);
        Assert.Equal(AdviserOpinion.NoAdviser,Engine(World()).GetPersonnelJourney(1,11,1,PersonnelJourneyKind.Search).Advice.Opinion);
        Assert.True(engine.Execute(new("player"),new SearchCommand(Guid.NewGuid(),1,0,11,30)).Submission.Accepted);
        Assert.False(engine.GetPersonnelJourney(1,11,30,PersonnelJourneyKind.Search).Available);
    }
    [Theory]
    [InlineData(OfficerStatus.Serving)]
    [InlineData(OfficerStatus.Captive)]
    public void BarbarianAffiliationRejectsRecruitmentWithoutChangingState(OfficerStatus status)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        mutation.Replace(mutation.Officers[20].WithLoyalty(1, 200).WithLoyalty(2, 0) with
            { Status = status, SettlementId = status == OfficerStatus.Captive ? 1 : 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        string before = engine.Snapshot.StateHash;

        Assert.False(Recruit(engine).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void TargetJoiningBarbarianForceCancelsPendingRecruitmentAndReturnsActor()
    {
        var world = World();
        var engine = Engine(world);
        Assert.True(Recruit(engine).Submission.Accepted);
        Guid taskId = Assert.Single(engine.Snapshot.Simulation.Tasks).Id;
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        world.Commit(mutation);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));

        var events = Advance(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
        Assert.Single(events.OfType<TaskCancelledEvent>(), e => e.TaskId == taskId);
        Assert.DoesNotContain(events, e => e is RecruitmentResolvedEvent);
        Assert.Equal(2, engine.Snapshot.Officers.Single(p => p.Id == 20).OwnerId);
        for (int xun = 0; xun < 5 && engine.Snapshot.Simulation.Tasks.Length > 0; xun++)
            Assert.Equal(CanonicalJson.Text(Advance(engine)), CanonicalJson.Text(Advance(restored)));
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Fact]
    public void PendingSearchKeepsDiscoveryRollWhileRunSeedCanChangeCandidateAndRestoreRepeatsResult()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[11] with { BaseAbilities = new(50, 50, 100, 200) });
        mutation.Replace(mutation.Officers[20] with
        {
            OwnerId = null, Status = OfficerStatus.Undiscovered, SettlementId = null, RegionId = 30
        });
        world.Commit(mutation);
        var original = Engine(world);
        Assert.True(original.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 11, 30)).Submission.Accepted);
        var pending = original.CaptureStableState();
        var task = Assert.Single(pending.Simulation.Tasks);
        HashSet<int> discovered = [];
        for (ulong runSeed = 0; runSeed < 16; runSeed++)
        {
            var state = pending with { Simulation = pending.Simulation with { RunSeed = runSeed, RandomStreams = [] } };
            var run = Engine(WorldState.Restore(state));
            var restored = Engine(WorldState.Restore(state));
            Assert.Equal(task, Assert.Single(run.Snapshot.Simulation.Tasks));
            var events = Advance(run);
            Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
            Assert.Equal(run.Snapshot.StateHash, restored.Snapshot.StateHash);
            var search = Assert.Single(events.OfType<SearchResolvedEvent>());
            Assert.NotNull(search.DiscoveredOfficerId);
            discovered.Add(search.DiscoveredOfficerId.Value);
        }
        Assert.Equal(new[] { 20, 21 }, discovered.Order().ToArray());
    }

    [Theory]
    [InlineData(false, 0UL)]
    [InlineData(false, 190UL)]
    [InlineData(true, 0UL)]
    [InlineData(true, 190UL)]
    public void SubmissionRollIgnoresRunSeedCommandIdAndOtherSubmissionOrder(bool search, ulong seed)
    {
        var direct = Engine(World(seed));
        var origin = direct.CaptureStableState();
        var loaded = Engine(WorldState.Restore(origin with
        {
            Simulation = origin.Simulation with { RunSeed = seed + 123, RandomStreams = [] }
        }));
        void Submit(SimulationEngine engine, int actor)
        {
            GameCommand command = search
                ? new SearchCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, actor, 30)
                : new RecruitOfficerCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, actor, 20);
            Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        }
        Submit(direct, 11);
        Submit(loaded, 12);
        Submit(loaded, 11);
        var expected = Assert.Single(direct.Snapshot.Simulation.Tasks).Journey!.SuccessRoll;
        Assert.Equal(expected, loaded.Snapshot.Simulation.Tasks.Single(t => t.OfficerId == 11).Journey!.SuccessRoll);
        Assert.Empty(loaded.Snapshot.Simulation.RandomStreams);
        Assert.Equal(origin.Simulation.RandomStreams, direct.Snapshot.Simulation.RandomStreams);
        var restored = Engine(WorldState.Restore(loaded.CaptureStableState()));
        Assert.Equal(expected, restored.Snapshot.Simulation.Tasks.Single(t => t.OfficerId == 11).Journey!.SuccessRoll);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void CaptiveRecruitmentRequiresOwnDetentionAndCanRecruitAboveOriginalLoyalty100(int detention, bool accepted)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[20].WithLoyalty(1, 200).WithLoyalty(2, 120) with
            { Status = OfficerStatus.Captive, SettlementId = detention });
        world.Commit(mutation);
        var engine = Engine(world);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(accepted, Recruit(engine).Submission.Accepted);
        if (!accepted) { Assert.Equal(before, engine.Snapshot.StateHash); return; }
        var events = Advance(engine);
        Assert.True(Assert.Single(events.OfType<RecruitmentResolvedEvent>()).Success);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 20).OwnerId);
        Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 20).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidTargetCancelsOnceAndReturnsToCurrentOrReplacementHome(bool loseSource)
    {
        var world = World();
        var engine = Engine(world);
        Assert.True(Recruit(engine).Submission.Accepted);
        Guid original = Assert.Single(engine.Snapshot.Simulation.Tasks).Id;
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[20] with { OwnerId = null, Status = OfficerStatus.Free, SettlementId = null, RegionId = 30 });
        if (loseSource) mutation.Replace(mutation.Settlements[1] with { Durability = 0 });
        world.Commit(mutation);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var events = Advance(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
        Assert.Single(events.OfType<TaskCancelledEvent>(), e => e.TaskId == original);
        Assert.DoesNotContain(events, e => e is RecruitmentResolvedEvent);
        Assert.Equal(loseSource ? 3 : 1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        for (int xun = 0; xun < 5 && engine.Snapshot.Simulation.Tasks.Length > 0; xun++)
            Assert.Equal(CanonicalJson.Text(Advance(engine)), CanonicalJson.Text(Advance(restored)));
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    private static readonly PersonnelRulebook Places = new([new(1, MapCoordinates.Center(new(0, 0))), new(2, MapCoordinates.Center(new(90, 0))), new(3, MapCoordinates.Center(new(0, 1))), new(30, MapCoordinates.Center(new(30, 30)))]);
    private static WorldState World(ulong seed = 190) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 1000) { LeaderId = 1, BaseTrust = 1000 }, new(2, 0) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(3, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Affinity = 50 },
         new(11, 1, 1, new(50, 50, 100, 50), false), new(12, 1, 1, new(50, 50, 100, 50), false),
         new(13, 1, 3, new(50, 50, 100, 50), false),
         new(20, 2, 2, new(50, 50, 50, 50), false) { ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 90), Righteousness = 5 },
         new(21, null, null, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered, RegionId = 30, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 0) }],
        relations: [new(1, 2, 80)], controls: [new("player", 1, ControllerKind.Player)], cityIndependenceEnabled: false, corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }]);
    private static SimulationEngine Engine(WorldState world) => new(world, EconomyRulebook.Empty, Places);
    private static CommandExecutionResult Recruit(SimulationEngine engine, int actor = 11, int target = 20) =>
        engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, actor, target));
    private static IReadOnlyList<DomainEvent> Advance(SimulationEngine engine, bool acceptSearchRecruitment = true)
    {
        engine.EndControl(new("player"));
        List<DomainEvent> events = [.. engine.AdvanceXun().Events];
        while (engine.GetSearchRecruitmentPrompt() is { } prompt)
        {
            var response = new RespondDailyTaskCommand(SimulationEngineTests.Id(100000 + (int)engine.Snapshot.Simulation.NextSequence),
                prompt.ForceId, engine.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, acceptSearchRecruitment);
            var result = engine.Execute(new("player"), response);
            Assert.True(result.Submission.Accepted);
            events.AddRange(result.Events);
        }
        return events;
    }

    [Fact]
    public void RecruitmentWaitsAndRestoresSubmissionRollThenChangesAffiliation()
    {
        var engine = Engine(World());
        Assert.True(Recruit(engine).Submission.Accepted);
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(new GameClock(190, 1, 15), task.Due);
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
        Assert.Equal(2, restored.Snapshot.Simulation.Tasks.Count(t => t.Kind == "personnel-return"));
        Assert.Equal(CanonicalJson.Text(Advance(engine)), CanonicalJson.Text(Advance(restored)));
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
        Assert.Equal(3, engine.Snapshot.Simulation.Tasks.Count(t => t.Kind == "personnel-return"));
        Advance(engine);
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
        change.Replace(change.Forces[2] with { LeaderId = null }); change.Replace(ResourceAccounts.Root(change, 2) with { LeaderId = null });
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
        PersonnelTransitions.Recruit(change, 11, 2, 2, events, Places, false);
        world.Commit(change);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(2, engine.Snapshot.Officers.Single(p => p.Id == 11).OwnerId);
    }

    [Fact]
    public void SearchDiscoversOnlyAtDestinationAndCanBeFollowedByRecruitment()
    {
        var observer = new DiscoveryObserver { Personnel = Places };
        var engine = new SimulationEngine(World(), observer);
        bool found = false;
        for (int attempt = 0; attempt < 10 && !found; attempt++)
        {
            Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, 11, 30)).Submission.Accepted);
            var events = Advance(engine);
            found = Assert.Single(events.OfType<SearchResolvedEvent>()).DiscoveredOfficerId == 21;
            while (engine.Snapshot.Simulation.Tasks.Any(t => t.OfficerId == 11)) Advance(engine);
        }
        Assert.True(found);
        var target = engine.Snapshot.Officers.Single(p => p.Id == 21);
        Assert.Equal(OfficerStatus.Free, target.Status);
        Assert.Null(target.OwnerId);
        Assert.Null(target.SettlementId);
        Assert.Equal(30, target.RegionId);
        Assert.Equal(49, target.LoyaltyTo(1));
        Assert.Equal(190, target.StatusStartYear);
        Assert.True(observer.DiscoveredWithActionConsumed);
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

    private sealed class DiscoveryObserver : SimulationRules
    {
        internal bool DiscoveredWithActionConsumed { get; private set; }
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.DailyCommands && world.Officers[21].Status == OfficerStatus.Free)
                DiscoveredWithActionConsumed |= world.Officers[21].Acted;
        }
    }

    [Fact]
    public void AppointmentPreservesJourneyButRewardWaitsForArrivalThroughRestore()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[11].WithLoyalty(1, 80) with { BaseAbilities = new(50, 80, 100, 50) });
        world.Commit(mutation);
        var engine = Engine(world);
        Assert.True(engine.Execute(new("player"), new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [11], 3)).Submission.Accepted);
        var journey = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.True(engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 1, 11)).Submission.Accepted);
        string beforeReward = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.PersonUnavailable,
            engine.Execute(new("player"), new RewardOfficerCommand(Guid.NewGuid(), 1, 2, 11)).Submission.Rejection);
        Assert.Equal(beforeReward, engine.Snapshot.StateHash);
        Assert.Equal(journey, Assert.Single(engine.Snapshot.Simulation.Tasks));
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        int loyaltyBefore = restored.Snapshot.Officers.Single(p => p.Id == 11).Loyalty;
        var continuedEvents = Advance(engine);
        Assert.Equal(CanonicalJson.Text(continuedEvents), CanonicalJson.Text(Advance(restored)));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        var person = restored.Snapshot.Officers.Single(p => p.Id == 11);
        Assert.Equal(OfficerStatus.Strategist, person.Status);
        Assert.Equal(3, person.SettlementId);
        Assert.Equal(loyaltyBefore + continuedEvents.OfType<OfficerLoyaltyChangedEvent>()
            .Where(e => e.OfficerId == 11 && e.ForceId == 1 && e.Reason is "paid-salary" or "unpaid-salary")
            .Sum(e => e.Current - e.Previous), person.Loyalty);
        Assert.Empty(restored.Snapshot.Simulation.Tasks);
        Assert.True(restored.Execute(new("player"), new RewardOfficerCommand(Guid.NewGuid(), 1,
            restored.Snapshot.Simulation.NextSequence, 11)).Submission.Accepted);
        Assert.Equal(person.Loyalty + 10, restored.Snapshot.Officers.Single(p => p.Id == 11).Loyalty);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("search")]
    [InlineData("recruit")]
    public void RewardRejectsEveryPersonnelJourneyWithoutChangingState(string kind)
    {
        var engine = Engine(World());
        Guid id = Guid.NewGuid();
        GameCommand journey = kind switch
        {
            "move" => new MoveOfficersCommand(id, 1, 0, [11], 3),
            "search" => new SearchCommand(id, 1, 0, 11, 30),
            _ => new RecruitOfficerCommand(id, 1, 0, 11, 20)
        };
        Assert.True(engine.Execute(new("player"), journey).Submission.Accepted);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        foreach (var current in new[] { engine, restored })
        {
            string before = current.Snapshot.StateHash;
            var reward = new RewardOfficerCommand(Guid.NewGuid(), 1, current.Snapshot.Simulation.NextSequence, 11);
            Assert.Equal(CommandRejection.PersonUnavailable, current.Execute(new("player"), reward).Submission.Rejection);
            Assert.Equal(before, current.Snapshot.StateHash);
        }
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
            Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
                engine.Snapshot.Simulation.NextSequence, 11, 30)).Submission.Accepted);
            events.AddRange(Advance(engine));
            events.AddRange(Advance(engine));
        }
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 21).OwnerId);
        Assert.Contains(events, e => e is OfficerLoyaltyChangedEvent { Previous: 40, Current: 60 });
        Assert.Contains(events, e => e is SearchResolvedEvent { Recruited: true });
    }

    [Fact]
    public void DestroyedOriginRejectsSearchAndRecruitmentWithoutChangingState()
    {
        var world = World();
        var engine = Engine(world);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { Durability = 0 });
        world.Commit(mutation);
        string before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 11, 1)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.False(Recruit(engine).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(30, 0)]
    [InlineData(30, 30)]
    [InlineData(30, 300)]
    public void SearchReturnsBeforeNextOrdersOnlyWhenReturnDistanceIsZero(int destination, int distance)
    {
        var places = new PersonnelRulebook([new(1, new(0, 0)), new(2, new(900, 0)),
            new(3, new(30, 0)), new(30, new(distance, 0))], new Dictionary<int, int> { [30] = 1 });
        var engine = new SimulationEngine(World(), EconomyRulebook.Empty, places);
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 1, destination)).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, places);
        var events = Advance(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Advance(restored)));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(new GameClock(190, 1, 10), Assert.Single(events.OfType<SearchResolvedEvent>()).Clock);
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.False(engine.Snapshot.Officers.Single(p => p.Id == 1).Acted);
        if (distance == 0)
        {
            Assert.Empty(engine.Snapshot.Simulation.Tasks);
            var arrival = Assert.Single(events.OfType<PersonnelReturnedEvent>());
            Assert.Equal(new GameClock(190, 1, 11), arrival.Clock);
            int map = events.ToList().FindIndex(e => e is StageCompletedEvent
                { Clock: { Day: 11 }, Stage: ResolutionStage.DailyMapUpdate });
            int units = events.ToList().FindIndex(e => e is StageCompletedEvent
                { Clock: { Day: 11 }, Stage: ResolutionStage.UnitAndBuildingEvents });
            Assert.True(map < events.ToList().IndexOf(arrival) && events.ToList().IndexOf(arrival) < units);
            Assert.True(engine.Snapshot.Officers.Single(p => p.Id == 1).IsCommander);
        }
        else
        {
            Assert.Single(engine.Snapshot.Simulation.Tasks);
            Assert.Empty(events.OfType<PersonnelReturnedEvent>());
        }
        Assert.Equal(distance == 0, engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 1, 1, 1)).Submission.Accepted);
    }

    [Theory]
    [InlineData(null, null, true, true)]
    [InlineData(null, 1, true, true)]
    [InlineData(null, 2, true, false)]
    [InlineData(1, 2, true, true)]
    [InlineData(2, 1, true, false)]
    [InlineData(null, null, false, true)]
    [InlineData(null, 1, false, true)]
    [InlineData(null, 2, false, false)]
    public void SearchTargetsUseTheirOwnerThenTheUnownedRegionsParentCity(int? targetOwner, int? cityOwner, bool built, bool accepted)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 0) { LeaderId = 1 }, new(2, 0) { LeaderId = 2 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100),
             new(3, cityOwner, 0, 100, 100, 0, 0, 0, 100, 0, 100),
             .. built ? new[] { new SettlementState(30, targetOwner, 0, 100, 100, 0, 0, 0, 100, 0, 100)
                 { Kind = SettlementKind.Facility } } : []],
            [new(1, 1, 1, new(50, 50, 1, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 10000)) { IsRoot = true }]);
        var places = new PersonnelRulebook(Places.Places.Values, new Dictionary<int, int> { [30] = 3 });
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, places);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(accepted, engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 1, 30)).Submission.Accepted);
        if (!accepted) Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void SearchRechecksParentCityOwnershipBeforeResolving()
    {
        var world = World();
        var places = new PersonnelRulebook(Places.Places.Values, new Dictionary<int, int> { [30] = 3 });
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, places);
        Assert.True(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 11, 30)).Submission.Accepted);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[13] with { SettlementId = 1 });
        mutation.Replace(mutation.Settlements[3] with { OwnerId = 2 });
        world.Commit(mutation);
        var events = Advance(engine);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(events.OfType<SearchResolvedEvent>());
        Assert.Equal(OfficerStatus.Undiscovered, engine.Snapshot.Officers.Single(p => p.Id == 21).Status);
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
        Assert.All(events.OfType<PersonnelReturnedEvent>(), e => Assert.Equal(task.Started.NextDay(), e.Clock));
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
    }

    [Theory]
    [InlineData(PersonnelJourneyKind.Recruit, 0, 1)]
    [InlineData(PersonnelJourneyKind.Recruit, 30, 1)]
    [InlineData(PersonnelJourneyKind.Recruit, 300, 10)]
    [InlineData(PersonnelJourneyKind.Recruit, 301, 11)]
    [InlineData(PersonnelJourneyKind.Move, 30, 1)]
    [InlineData(PersonnelJourneyKind.Move, 300, 10)]
    [InlineData(PersonnelJourneyKind.Search, 0, 10)]
    [InlineData(PersonnelJourneyKind.Search, 301, 20)]
    public void CountdownIncludesFirstDailyStageAndResolvesBeforeDateAdvance(PersonnelJourneyKind kind, int distance, int expectedDay)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[20] with { OwnerId = null, Status = OfficerStatus.Free, SettlementId = null, RegionId = 30 });
        world.Commit(mutation);
        var places = new PersonnelRulebook([new(1, new(0, 0)), new(2, new(900, 0)), new(3, new(distance, 0)), new(30, new(distance, 0))]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, places);
        var id = Guid.NewGuid();
        GameCommand command = kind switch
        {
            PersonnelJourneyKind.Recruit => new RecruitOfficerCommand(id, 1, 0, 11, 20),
            PersonnelJourneyKind.Move => new MoveOfficersCommand(id, 1, 0, [11], 3),
            _ => new SearchCommand(id, 1, 0, 11, 30)
        };
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(new GameClock(190, 1, expectedDay), Assert.Single(engine.Snapshot.Simulation.Tasks).Due);
        List<DomainEvent> events = [];
        for (int xun = 0; xun < (expectedDay + 9) / 10; xun++)
        {
            var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, places);
            var current = Advance(engine);
            Assert.Equal(CanonicalJson.Text(current), CanonicalJson.Text(Advance(restored)));
            Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
            events.AddRange(current);
            if (expectedDay > (xun + 1) * 10)
            {
                Assert.Single(engine.Snapshot.Simulation.Tasks);
                Assert.Empty(events.OfType<TaskCompletedEvent>());
                Assert.False(engine.Snapshot.Officers.Single(p => p.Id == 11).Acted);
                Assert.False(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 1, 11, 30)).Submission.Accepted);
            }
        }
        var completed = Assert.Single(events.OfType<TaskCompletedEvent>(), e => e.TaskId == id);
        Assert.Equal(new GameClock(190, 1, expectedDay), completed.Clock);
        int completion = events.IndexOf(completed);
        int before = events.FindIndex(e => e is StageCompletedEvent s && s.Clock == completed.Clock && s.Stage == ResolutionStage.BeforeDailyCommands);
        int after = events.FindIndex(e => e is StageCompletedEvent s && s.Clock == completed.Clock && s.Stage == ResolutionStage.DailyCommands);
        Assert.True(before < completion && completion < after);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Tasks, t => t.Id == id);
    }
}
