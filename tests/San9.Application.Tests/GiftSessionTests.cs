using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class GiftSessionTests
{
    [Fact]
    public void AuthoredDiplomacyPreferencesAreValidatedMappedAndFingerprintRelevant()
    {
        var content = Content();
        var treasure = content.Information.Treasures[0] with { FavoredOwnerIds = [21, 11], AutomaticDiplomacyOfferAllowed = false };
        var modified = content with { Information = content.Information with { Treasures = [treasure] } };
        var original = GameSession.Start(content, 42);
        foreach (var changed in new[] { content.Information.Treasures[0] with { FavoredOwnerIds = [11] },
            content.Information.Treasures[0] with { RetentionGroup = TreasureRetentionGroup.War },
            content.Information.Treasures[0] with { AutomaticDiplomacyOfferAllowed = false } })
            Assert.NotEqual(original.ContentFingerprint, GameSession.Start(content with
                { Information = content.Information with { Treasures = [changed] } }, 42).ContentFingerprint);
        var session = GameSession.Start(modified, 42);
        Assert.NotEqual(original.ContentFingerprint, session.ContentFingerprint);
        var reordered = modified with { Information = modified.Information with { Treasures = [treasure with { FavoredOwnerIds = [11, 21] }] } };
        Assert.Equal(session.ContentFingerprint, GameSession.Start(reordered, 42).ContentFingerprint);
        var rule = treasure.ToRule();
        Assert.False(rule.AutomaticDiplomacyOfferAllowed);
        Assert.Equal(treasure.FavoredOwnerIds, rule.FavoredOwnerIds);
        foreach (var ids in new System.Collections.Immutable.ImmutableArray<int>[] { [999], [11, 11], default })
            Assert.Throws<InvalidDataException>(() => (modified with { Information = modified.Information with
                { Treasures = [treasure with { FavoredOwnerIds = ids }] } }).Validate());
        Assert.Throws<SaveException>(() => SaveService.Decode(SaveService.Encode(original), modified));
    }

    [Theory]
    [InlineData("favoredOwnerIds")]
    [InlineData("automaticDiplomacyOfferAllowed")]
    [InlineData("retentionGroup")]
    public void TreasureDiplomacyFieldsCannotBeSilentlyDefaultedWhenMissing(string field)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(CanonicalJson.Text(Content().Information.Treasures[0]))!;
        Assert.True(json.AsObject().Remove(field));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<TreasureDefinition>(
            json.ToJsonString(), CanonicalJson.Options));
    }

    private static ScenarioInitialState Content() => new(ScenarioInitialState.CurrentSchemaVersion, "gifts", 190, 1, 1, 1,
        [new(1, "first", 1, 100) { LeaderId = 11 },
         new(2, "second", 2, 100) { LeaderId = 21 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(2, "away", 2, 18, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("ruler", new(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("envoy", new(12, 1, 1, new(50, 50, 100, 50), false)),
            new("ruler2", new(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
            new("envoy2", new(22, 2, 2, new(50, 50, 100, 50), false))],
        Information = new([], [], [new(9, "treasure", 0, 0, 50, null)], [new San9.Core.Domain.CorpsState(1, 1, 11, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(100000, 100000)) { IsRoot = true }], [new(9, 11, TreasureDiscovery.Discovered, null)])
    };

    [Theory]
    [InlineData(GiftKind.Money, 20000u)]
    [InlineData(GiftKind.Food, 200000u)]
    [InlineData(GiftKind.Treasure, 0u)]
    public void GiftPaysOnceTransfersToAiAndReturnsWithSaveAndReplay(GiftKind kind, uint amount)
    {
        var content = Content();
        content = content with { Information = content.Information with { Corps = content.Information.Corps.Select(f => f with { Treasury = f.Treasury with { Food = 500000 } }).ToArray() } };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        var offer = new GiftOffer(kind, amount, kind == GiftKind.Treasure ? 9 : null);
        string beforeAdvice = session.Capture().StateHash;
        Assert.Equal(session.GetGiftAdvice(1, 12, 2, offer), session.GetGiftAdvice(1, 12, 2, offer));
        Assert.Equal(beforeAdvice, session.Capture().StateHash);
        uint before = kind == GiftKind.Food ? session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food : session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money;
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, offer)).Submission.Accepted);
        Assert.Single(session.Snapshot.Simulation.Tasks);
        if (kind == GiftKind.Treasure) Assert.Equal(12, session.Snapshot.Treasures[0].HolderId);
        else Assert.Equal(before - amount, kind == GiftKind.Food ? session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food : session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.True(gift.Accepted);
        Assert.Equal(amount, gift.Received);
        Assert.Equal(0u, gift.Spent);
        Assert.Equal(0u, gift.Refunded);
        Assert.Equal(60, Assert.Single(session.Snapshot.Relations).Value);
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        if (kind == GiftKind.Treasure) Assert.Equal(21, session.Snapshot.Treasures[0].HolderId);
        var returning = SaveService.Decode(SaveService.Encode(session), content);
        var next = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(next.Events), CanonicalJson.Text(returning.AdvanceXun().Events));
        Assert.Contains(next.Events.OfType<PersonnelReturnedEvent>(), e => e.OfficerId == 12 && e.Clock == new GameClock(190, 1, 13));
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 12);
        DecisionResponses.DeclineAll(session);
        DecisionResponses.DeclineAll(returning);
        Assert.Equal(session.Capture().StateHash, returning.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Theory]
    [InlineData(true, GiftKind.Money)]
    [InlineData(false, GiftKind.Money)]
    [InlineData(true, GiftKind.Food)]
    [InlineData(false, GiftKind.Food)]
    [InlineData(true, GiftKind.Treasure)]
    [InlineData(false, GiftKind.Treasure)]
    public void PlayerAcceptsOrRejectsInMemoryAndReplaysFromOutboundSave(bool accept, GiftKind kind)
    {
        var content = Content();
        content = content with { Information = content.Information with
            { TreasureStates = [new(9, 21, TreasureDiscovery.Discovered, null)] } };
        uint amount = kind == GiftKind.Treasure ? 0u : 1000u;
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1,
            new(kind, amount, kind == GiftKind.Treasure ? 9 : null))).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        restored.AdvanceXun();
        var arriving = session.AdvanceXun();
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.DoesNotContain(arriving.Events, e => e is XunAdvancedEvent);
        Assert.Empty(arriving.Events.OfType<GiftResolvedEvent>());
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        Assert.Equal(TaskResolutionPhase.XunBoundary, pending.Phase);
        Assert.Equal(1, pending.RespondingForceId);
        Assert.Equal(AdviserOpinion.Unavailable, session.GetGiftAdvice(2, 21, 1, new(GiftKind.Money, 1000, null)).Opinion);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            pending.RemainingTaskIds[0], 0, accept);
        var result = session.Execute(new("player"), response);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("player"), response).Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.Equal(accept, gift.Accepted);
        Assert.Equal(accept ? amount : 0, gift.Received);
        Assert.Equal(accept ? 0 : amount, gift.Refunded);
        Assert.Equal(0u, gift.Spent);
        Assert.Equal(amount, gift.Received + gift.Refunded + gift.Spent);
        if (kind == GiftKind.Treasure)
        {
            Assert.Equal(accept ? 11 : 22, session.Snapshot.Treasures.Single().HolderId);
            Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-refused");
        }
        else if (!accept)
        {
            var refund = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-refused");
            Assert.Equal(new ResourceAccount(ResourceOwnerKind.Corps, 2), refund.Account);
            Assert.Equal(kind == GiftKind.Money ? ResourceKind.Money : ResourceKind.Food, refund.Resource);
            Assert.Equal(amount, refund.Actual);
            Assert.Equal(0, refund.SaturationLoss);
            Assert.DoesNotContain(result.Events.OfType<OfficerAbilityGrownEvent>(), e => e.OfficerId == 22);
        }
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        var settled = session.Capture().StateHash;
        var repeated = response with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
        Assert.False(session.Execute(new("player"), repeated).Submission.Accepted);
        Assert.Equal(settled, session.Capture().StateHash);
        var returning = SaveService.Decode(SaveService.Encode(session), content);
        var home = session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(home.Events), CanonicalJson.Text(returning.AdvanceXun().Events));
        Assert.DoesNotContain(home.Events.OfType<ResourceChangedEvent>(), e => e.Reason is "gift-refused" or "gift-cancelled");
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 22);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void BoundaryContinuationRejectsMissingWrongOrUnknownPhaseAndNonXunTravel()
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        session.AdvanceXun();
        var snapshot = new SessionSnapshot(session.Snapshot, [], [], session.NextOperationSequence);
        var pending = snapshot.World.Simulation.PendingDailyTask!;
        foreach (var phase in new[] { TaskResolutionPhase.DailyCommands, (TaskResolutionPhase)99 })
        {
            var changed = snapshot with { World = snapshot.World with { Simulation = snapshot.World.Simulation with
                { PendingDailyTask = pending with { Phase = phase, XunStarted = snapshot.World.Clock } } } };
            Assert.Throws<ArgumentException>(() => GameSession.Restore(content, changed));
        }
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);

        var fresh = GameSession.Start(content, 42);
        Assert.True(fresh.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        var outbound = fresh.Capture();
        var task = Assert.Single(outbound.World.Simulation.Tasks);
        Assert.Throws<ArgumentException>(() => GameSession.Restore(content, outbound with { World = outbound.World with
            { Simulation = outbound.World.Simulation with { Tasks = [task with { Due = new(190, 1, 3) }] } } }));
    }

    [Fact]
    public void FixedSubmissionRollIgnoresUnrelatedCommandsAndRunSeed()
    {
        var content = Content();
        var first = GameSession.Start(content, 42);
        var second = GameSession.Start(content, 42);
        Assert.True(second.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 1, 0)).Submission.Accepted);
        var snapshot = second.Capture();
        second = GameSession.Restore(content, snapshot with { World = snapshot.World with
            { Simulation = snapshot.World.Simulation with { RunSeed = 900 } } });
        var offer = new GiftOffer(GiftKind.Money, 1000, null);
        Assert.True(first.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, offer)).Submission.Accepted);
        Assert.True(second.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 1, 12, 2, offer)).Submission.Accepted);
        Assert.Equal(Assert.Single(first.Snapshot.Simulation.Tasks).Journey!.SuccessRoll,
            Assert.Single(second.Snapshot.Simulation.Tasks).Journey!.SuccessRoll);
    }

    [Theory]
    [InlineData(14, 100, true)]
    [InlineData(15, 100, false)]
    [InlineData(14, 0, false)]
    public void AiUsesStrictThresholdAndLivePolitics(int roll, int politics, bool accepted)
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        var snapshot = session.Capture();
        var task = Assert.Single(snapshot.World.Simulation.Tasks);
        session = GameSession.Restore(content, snapshot with { World = snapshot.World with
        {
            Officers = snapshot.World.Officers.Select(o => o.Id == 12 ? o with
                { BaseAbilities = o.BaseAbilities with { Politics = politics } } : o).ToImmutableArray(),
            Simulation = snapshot.World.Simulation with { Tasks = [task with { Journey = task.Journey! with { SuccessRoll = roll } }] }
        } });
        var result = session.AdvanceXun();
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.Equal(accepted, gift.Accepted);
        Assert.Equal(accepted ? 0u : 1000u, gift.Refunded);
        Assert.Equal(0u, gift.Spent);
        Assert.Equal(accepted ? 0 : 1, result.Events.OfType<ResourceChangedEvent>().Count(e => e.Reason == "gift-refused"));
    }

    [Fact]
    public void TargetCapacityReportsEveryUndeliveredUnitAsSpent()
    {
        var content = Content();
        content = content with { Information = content.Information with { Corps = content.Information.Corps.Select(f => f.IsRoot && f.OwnerId == 2 ? f with { Treasury = f.Treasury with { Money = 1000000 } } : f).ToArray() } };
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 20000, null))).Submission.Accepted);
        // Automatic economic work can spend the recipient's money before the envoy arrives.
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new PrepareAdvanceOperation())).Disposition);
        uint room = 1000000 - session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money;
        var result = Assert.Single(session.AdvanceXun().Events.OfType<GiftResolvedEvent>());
        Assert.True(result.Accepted);
        Assert.Equal(room, result.Received);
        Assert.Equal(20000u, result.Received + result.Spent);
    }

    [Theory]
    [InlineData(GiftKind.Money)]
    [InlineData(GiftKind.Food)]
    public void RefusalRefundHonorsSourceCapacityAndReportsOnlyOverflowAsSpent(GiftKind kind)
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1,
            new(kind, 1000, null))).Submission.Accepted);
        session.AdvanceXun();
        var saved = session.Snapshot;
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(saved with
        {
            Corps = saved.Corps.Select(c => !c.IsRoot || c.OwnerId != 2 ? c : c with { Treasury = kind == GiftKind.Money
                ? c.Treasury with { Money = 999900 } : c.Treasury with { Food = 999900 } }).ToArray()
        }));
        var pending = engine.Snapshot.Simulation.PendingDailyTask!;
        var result = engine.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, pending.RemainingTaskIds[0], 0, false));
        Assert.True(result.Submission.Accepted);
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.False(gift.Accepted);
        Assert.Equal(0u, gift.Received);
        Assert.Equal(100u, gift.Refunded);
        Assert.Equal(900u, gift.Spent);
        Assert.Equal(gift.Offer.Amount, gift.Received + gift.Refunded + gift.Spent);
        var refund = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-refused");
        Assert.Equal(100, refund.Actual);
        Assert.Equal(900, refund.SaturationLoss);
        var world = WorldState.Restore(engine.Snapshot).BeginMutation();
        List<DomainEvent> cancelled = [];
        var returning = Assert.Single(world.Simulation.Tasks, t => t.OfficerId == 22);
        TaskCancellation.Apply(world, returning, "test-return-cancel", cancelled);
        Assert.DoesNotContain(cancelled.OfType<ResourceChangedEvent>(), e => e.Reason is "gift-refused" or "gift-cancelled");
    }

    [Fact]
    public void InvalidOffersAndInsufficientBalanceAreRejectedWithoutOccupationOrPayment()
    {
        var session = GameSession.Start(Content(), 42);
        string before = session.Capture().StateHash;
        GiftOffer[] offers = [new(GiftKind.Money, 0, null), new(GiftKind.Money, 10, 9), new(GiftKind.Treasure, 1, 9),
            new(GiftKind.Treasure, 0, null), new((GiftKind)99, 1, null), new(GiftKind.Food, 1000000, null)];
        foreach (var offer in offers)
            Assert.False(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, offer)).Submission.Accepted);
        Assert.Equal(before, session.Capture().StateHash);
    }

    [Fact]
    public void CancellationAndTerminalCleanupSettleTheCurrentParcelAtMostOnce()
    {
        var session = GameSession.Start(Content(), 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 5000, null))).Submission.Accepted);
        var world = WorldState.Restore(session.Snapshot).BeginMutation();
        var task = Assert.Single(world.Simulation.Tasks);
        var stale = task with { Gift = new(2, new(GiftKind.Money, 10000, null)) };
        var events = new List<DomainEvent>();
        TaskCancellation.Apply(world, stale, "test-cancel", events);
        TaskCancellation.Apply(world, task, "test-cancel", events);
        world.SetSimulation(world.Simulation with { TerminalReason = "test-end" });
        Assert.False(ResolutionPipeline.CheckTerminal(world, events));
        Assert.Equal(5000, Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-cancelled").Actual);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(world.Simulation.Tasks);
    }

    [Fact]
    public void InvalidatedTargetRefundsPrepaidResourcesAndDoesNotApplyGiftEffects()
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 5000, null))).Submission.Accepted);
        var saved = session.Capture();
        session = GameSession.Restore(content, saved with { World = saved.World with
            { Forces = saved.World.Forces.Select(f => f.Id == 2 ? f with { LeaderId = null } : f).ToImmutableArray() } });
        var result = session.AdvanceXun();
        Assert.Empty(result.Events.OfType<GiftResolvedEvent>());
        var refund = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-cancelled");
        Assert.Equal(5000, refund.Actual);
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        Assert.DoesNotContain(session.AdvanceXun().Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-cancelled");
    }
}
