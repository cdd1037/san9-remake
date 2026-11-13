using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class GiftSessionTests
{
    [Fact]
    public void EquipmentScreenExplainsIncompatibleWeaponsAndDoesNotMoveInventory()
    {
        var basis = Content();
        var main = new EquipmentDefinition("spear", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)
            { Name = "枪", CompatibleWeapons = ["shield"] };
        var shield = new EquipmentDefinition("shield", EquipmentSlot.OffHand, EquipmentRarity.Ordinary, default, default) { Name = "盾" };
        var bow = new EquipmentDefinition("bow", EquipmentSlot.OffHand, EquipmentRarity.Ordinary, default, default) { Name = "弓" };
        var content = basis with { Information = basis.Information with { Equipment = [main, shield, bow] } };
        var initial = GameSession.Start(content, 42).Capture();
        var session = GameSession.Restore(content, initial with { World = initial.World with
        {
            Equipment = [.. initial.World.Equipment, new EquipmentInstance(500, "spear", 11, null, true),
                new EquipmentInstance(501, "bow", null, 1, false)]
        } });
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new Dictionary<string, string> { ["treasure"] = "名马" });
        var offered = Assert.Single(San9.Application.Presentation.OfficerEquipment.Read(session, texts, 11)
            .Slots.Single(slot => slot.Slot == EquipmentSlot.OffHand).Items);
        Assert.False(offered.CanEquip);
        Assert.Contains("主武器「枪」不兼容", offered.UnavailableReason);
        Assert.False(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 11, EquipmentSlot.OffHand, offered.Id)).Submission.Accepted);
        var unchanged = session.Snapshot.Equipment.Single(item => item.Id == offered.Id);
        Assert.Equal(1, unchanged.InventoryForceId);
        Assert.Null(unchanged.OfficerId);
    }

    [Fact]
    public void OfficerEquipmentShowsNineSlotsInventoryAndAppliesSelection()
    {
        var basis = Content();
        var named = new EquipmentDefinition("named-spear", EquipmentSlot.MainHand, EquipmentRarity.Rare, new(2, 0, 0, 0), default) { Name = "名枪" };
        var content = basis with { Information = basis.Information with { Equipment = basis.Information.Equipment.Add(named) } };
        var session = GameSession.Start(content, 42);
        var saved = session.Capture();
        session = GameSession.Restore(content, saved with { World = saved.World with
        { Equipment = [.. saved.World.Equipment, new EquipmentInstance(500, "named-spear", null, 1, false)] } });
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new Dictionary<string, string> { ["treasure"] = "名马" });
        var data = San9.Application.Presentation.OfficerEquipment.Read(session, texts, 11);
        Assert.True(data.CanManage);
        Assert.Equal(9, data.Slots.Length);
        Assert.Equal("名马", Assert.Single(data.Slots.Single(s => s.Slot == EquipmentSlot.Mount).Items).Name);
        var spear = Assert.Single(data.Slots.Single(s => s.Slot == EquipmentSlot.MainHand).Items);
        Assert.True(spear.InInventory);
        Assert.True(spear.CanEquip);
        Assert.True(session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 11, EquipmentSlot.MainHand, spear.Id)).Submission.Accepted);
        Assert.Equal(52, session.View.Officers.Single(o => o.Id == 11).Abilities.War);
        Assert.True(Assert.Single(San9.Application.Presentation.OfficerEquipment.Read(session, texts, 11)
            .Slots.Single(s => s.Slot == EquipmentSlot.MainHand).Items).Equipped);
        Assert.False(San9.Application.Presentation.OfficerEquipment.Read(session, texts, 21).CanManage);
    }

    [Fact]
    public void LegacySaveMigratesTreasureOnlyOnceAndPreservesUnequippedState()
    {
        var content = Content();
        var session = GameSession.Start(content, 42);
        var initial = Assert.Single(session.Snapshot.Equipment);
        Assert.Equal(9, initial.LegacyTreasureId);
        Assert.True(initial.Equipped);
        var old = session.Capture();
        old = old with { World = old.World with { Equipment = [], EquipmentInitialized = false } };
        var migrated = GameSession.Restore(content, old);
        Assert.True(migrated.Snapshot.EquipmentInitialized);
        Assert.True(Assert.Single(migrated.Snapshot.Equipment).Equipped);
        var command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, migrated.Snapshot.Simulation.NextSequence, 11, EquipmentSlot.Mount, null);
        Assert.True(migrated.Execute(new("player"), command).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(migrated), content);
        Assert.False(Assert.Single(restored.Snapshot.Equipment).Equipped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingBookEquipmentKeepsIdentityAndEquippedStateAcrossSave(bool equipped)
    {
        var basis = Content();
        var book = basis.Information.Treasures[0] with { Category = 2 };
        var legacy = new EquipmentDefinition("legacy-treasure:9", EquipmentSlot.Treasure,
            EquipmentRarity.Precious, book.AbilityBonuses, default) { LegacyTreasureId = 9 };
        var content = basis with { Information = basis.Information with { Treasures = [book], Equipment = [legacy] } };
        var session = GameSession.Start(content, 42);
        var saved = session.Capture();
        var oldShape = saved with { World = saved.World with
        {
            Equipment = [Assert.Single(saved.World.Equipment) with { Equipped = equipped }]
        } };
        var restored = GameSession.Restore(content, oldShape);
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new Dictionary<string, string> { ["treasure"] = "兵书" });
        var item = Assert.Single(San9.Application.Presentation.OfficerEquipment.Read(restored, texts, 11)
            .Slots.Single(slot => slot.Slot == EquipmentSlot.Book).Items);
        Assert.Equal(9, item.Id);
        Assert.Equal(equipped, item.Equipped);
        var loaded = SaveService.Decode(SaveService.Encode(restored), content);
        Assert.Equal(equipped, Assert.Single(loaded.Snapshot.Equipment).Equipped);
    }

    [Fact]
    public void PreviousBasicEquipmentCatalogGainsHiddenWeaponRecipe()
    {
        var basis = Content();
        var oldCatalog = EquipmentPreset.Basic().Where(item => item.Id != "basic-dart").ToImmutableArray();
        var content = basis with { Information = basis.Information with { Equipment = oldCatalog } };
        var session = GameSession.Start(content, 42);
        Assert.Contains(session.EquipmentRecipes, item => item.Id == "basic-dart" && item.Slot == EquipmentSlot.HiddenWeapon);
    }

    [Fact]
    public void AuthoredDiplomacyPreferencesAreValidatedMappedAndCanChangeWhenLoading()
    {
        var content = Content();
        var treasure = content.Information.Treasures[0] with { FavoredOwnerIds = [21, 11], AutomaticDiplomacyOfferAllowed = false };
        var modified = content with { Information = content.Information with { Treasures = [treasure] } };
        var original = GameSession.Start(content, 42);
        var rule = treasure.ToRule();
        Assert.False(rule.AutomaticDiplomacyOfferAllowed);
        Assert.Equal(treasure.FavoredOwnerIds, rule.FavoredOwnerIds);
        foreach (var ids in new System.Collections.Immutable.ImmutableArray<int>[] { [999], [11, 11], default })
            Assert.Throws<InvalidDataException>(() => (modified with { Information = modified.Information with
                { Treasures = [treasure with { FavoredOwnerIds = ids }] } }).Validate());
        StateAssert.Equal(original.Capture(), SaveService.Decode(SaveService.Encode(original), modified).Capture());
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
    [InlineData(GiftKind.Money, 10000u)]
    [InlineData(GiftKind.Food, 100000u)]
    [InlineData(GiftKind.Treasure, 0u)]
    public void GiftPaysOnceTransfersToAiAndReturnsWithSaveAndReplay(GiftKind kind, uint amount)
    {
        var content = Content();
        content = content with { Information = content.Information with { Corps = content.Information.Corps.Select(f => f with { Treasury = f.Treasury with { Food = 500000 } }).ToArray() } };
        var session = GameSession.Start(content, 42);
        var offer = new GiftOffer(kind, amount, kind == GiftKind.Treasure ? 9 : null);
        var beforeAdvice = session.Capture();
        Assert.Equal(session.GetGiftAdvice(1, 12, 2, offer), session.GetGiftAdvice(1, 12, 2, offer));
        StateAssert.Equal(beforeAdvice, session.Capture());
        uint before = kind == GiftKind.Food ? session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food : session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money;
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, offer)).Submission.Accepted);
        Assert.Single(session.Snapshot.Simulation.Tasks);
        if (kind == GiftKind.Treasure)
        {
            Assert.Equal(12, session.Snapshot.Treasures[0].HolderId);
            var equipment = Assert.Single(session.Snapshot.Equipment);
            Assert.Equal(12, equipment.OfficerId);
            Assert.False(equipment.Equipped);
        }
        else Assert.Equal(before - amount, kind == GiftKind.Food ? session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food : session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        // Fix the success roll: this case verifies payment and restoration, not acceptance probability.
        var outbound = session.Capture();
        session = GameSession.Restore(content, outbound with { World = outbound.World with
        {
            Simulation = outbound.World.Simulation with { Tasks = outbound.World.Simulation.Tasks.Select(t =>
                t with { Journey = t.Journey! with { SuccessRoll = 0 } }).ToImmutableArray() }
        } });
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.True(gift.Accepted);
        Assert.Equal(1, session.Snapshot.Officers.Single(p => p.Id == 12).ServiceMeritFor(1));
        Assert.Equal(amount, gift.Received);
        Assert.Equal(0u, gift.Spent);
        Assert.Equal(0u, gift.Refunded);
        Assert.Equal(60, Assert.Single(session.Snapshot.Relations).Value);
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        if (kind == GiftKind.Treasure)
        {
            Assert.Equal(21, session.Snapshot.Treasures[0].HolderId);
            Assert.Equal(21, Assert.Single(session.Snapshot.Equipment).OfficerId);
        }
        var returning = SaveService.Decode(SaveService.Encode(session), content);
        var next = session.AdvanceXun();
        EventAssert.Equal(next.Events, returning.AdvanceXun().Events);
        Assert.Contains(next.Events.OfType<PersonnelReturnedEvent>(), e => e.OfficerId == 12 && e.Clock == new GameClock(190, 1, 13));
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 12);
        DecisionResponses.DeclineAll(session);
        DecisionResponses.DeclineAll(returning);
        StateAssert.Equal(session.Capture(), returning.Capture());
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
        uint amount = kind == GiftKind.Treasure ? 0u : GiftOffer.MinimumAmount(kind);
        var session = GameSession.Start(content, 42);
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
        var draft = DecisionDraftTests.DiplomacyDraft(session, "gift");
        Assert.Equal("gift", draft.Role);
        Assert.Equal(new San9.Application.Presentation.DiplomacyMeeting(pending.RemainingTaskIds[0], 1, 22), draft.Meeting);
        Assert.Equal(pending.RemainingTaskIds[0], draft.Diplomacy!.TaskId);
        Assert.Equal(new GiftOffer(kind, amount, kind == GiftKind.Treasure ? 9 : null), draft.Diplomacy.Gift);
        Assert.Equal(session.GetGiftReceiptPreview(draft.Diplomacy.TaskId), draft.Diplomacy.GiftReceipt);
        var response = draft.Options.Single(o => o.Role == (accept ? "accept" : "decline"))
            .CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0);
        var result = session.Execute(new(draft.ControllerId), response);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), response).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.Equal(accept, gift.Accepted);
        Assert.Equal(accept ? 1 : 0, session.Snapshot.Officers.Single(p => p.Id == 22).ServiceMeritFor(2));
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
        var settled = session.Capture();
        var repeated = response with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
        Assert.False(session.Execute(new("player"), repeated).Submission.Accepted);
        StateAssert.Equal(settled, session.Capture());
        var returning = SaveService.Decode(SaveService.Encode(session), content);
        var home = session.AdvanceXun();
        EventAssert.Equal(home.Events, returning.AdvanceXun().Events);
        Assert.DoesNotContain(home.Events.OfType<ResourceChangedEvent>(), e => e.Reason is "gift-refused" or "gift-returned");
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.OfficerId == 22);
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
        content = content with { Information = content.Information with { Corps = content.Information.Corps.Select(f => f.IsRoot && f.OwnerId == 2 ? f with { Treasury = f.Treasury with { Money = TreasuryRules.Capacity - 50 } } : f).ToArray() } };
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(GiftKind.Money, 10000, null))).Submission.Accepted);
        // Public capacity settlement is independent of automatic economics and residual searches.
        var outbound = session.Snapshot;
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(outbound with
        {
            Simulation = outbound.Simulation with { Tasks = outbound.Simulation.Tasks.Select(t =>
                t with { Journey = t.Journey! with { SuccessRoll = 0 } }).ToImmutableArray() }
        }));
        foreach (var seat in engine.Snapshot.Simulation.Controls) engine.EndControl(new(seat.ControllerId));
        uint room = TreasuryRules.Capacity - engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money;
        var result = Assert.Single(engine.AdvanceXun().Events.OfType<GiftResolvedEvent>());
        Assert.True(result.Accepted);
        Assert.Equal(room, result.Received);
        Assert.Equal(10000u, result.Received + result.Spent);
    }

    [Theory]
    [InlineData(GiftKind.Money)]
    [InlineData(GiftKind.Food)]
    public void RefusalRefundHonorsSourceCapacityAndReportsOnlyOverflowAsSpent(GiftKind kind)
    {
        var content = Content();
        uint amount = GiftOffer.MinimumAmount(kind);
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1,
            new(kind, amount, null))).Submission.Accepted);
        session.AdvanceXun();
        var saved = session.Snapshot;
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(saved with
        {
            Corps = saved.Corps.Select(c => !c.IsRoot || c.OwnerId != 2 ? c : c with { Treasury = kind == GiftKind.Money
                ? c.Treasury with { Money = TreasuryRules.Capacity - 100 } : c.Treasury with { Food = TreasuryRules.Capacity - 100 } }).ToArray()
        }));
        var pending = engine.Snapshot.Simulation.PendingDailyTask!;
        var result = engine.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, pending.RemainingTaskIds[0], 0, false));
        Assert.True(result.Submission.Accepted);
        var gift = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.False(gift.Accepted);
        Assert.Equal(0u, gift.Received);
        Assert.Equal(100u, gift.Refunded);
        Assert.Equal(amount-100u, gift.Spent);
        Assert.Equal(gift.Offer.Amount, gift.Received + gift.Refunded + gift.Spent);
        var refund = Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-refused");
        Assert.Equal(100, refund.Actual);
        Assert.Equal((long)amount-100, refund.SaturationLoss);
        var world = WorldState.Restore(engine.Snapshot).BeginMutation();
        List<DomainEvent> cancelled = [];
        var returning = Assert.Single(world.Simulation.Tasks, t => t.OfficerId == 22);
        TaskCancellation.Apply(world, returning, "test-return-cancel", cancelled);
        Assert.DoesNotContain(cancelled.OfType<ResourceChangedEvent>(), e => e.Reason is "gift-refused" or "gift-returned");
    }

    [Fact]
    public void InvalidOffersAndInsufficientBalanceAreRejectedWithoutOccupationOrPayment()
    {
        var session = GameSession.Start(Content(), 42);
        var before = session.Capture();
        GiftOffer[] offers = [new(GiftKind.Money, 0, null), new(GiftKind.Money, 10, 9), new(GiftKind.Treasure, 1, 9),
            new(GiftKind.Treasure, 0, null), new((GiftKind)99, 1, null), new(GiftKind.Food, 1000000, null)];
        foreach (var offer in offers)
            Assert.False(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, offer)).Submission.Accepted);
        StateAssert.Equal(before, session.Capture());
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
        world.SetSimulation(world.Simulation with { TerminalReason = "player-defeated" });
        Assert.False(ResolutionPipeline.CheckTerminal(world, events));
        Assert.Empty(events.OfType<ResourceChangedEvent>());
        Assert.Equal(5000u, Assert.Single(events.OfType<ReturnCargoLostEvent>()).Amount);
        Assert.Single(events.OfType<TaskCancelledEvent>());
        Assert.Empty(world.Simulation.Tasks);
    }

    private static GameSession CancelledGift(GiftKind kind, out ScenarioInitialState content)
    {
        content = Content();
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2,
            new(kind, kind == GiftKind.Money ? 5000u : 50000u, null))).Submission.Accepted);
        var saved = session.Capture();
        session = GameSession.Restore(content, saved with { World = saved.World with
            { Forces = saved.World.Forces.Select(f => f.Id == 2 ? f with { LeaderId = null } : f).ToImmutableArray() } });
        var result = session.AdvanceXun();
        Assert.Empty(result.Events.OfType<GiftResolvedEvent>());
        Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason.StartsWith("gift-", StringComparison.Ordinal));
        Assert.NotNull(Assert.Single(session.Snapshot.Simulation.Tasks).ReturnCargo);
        return session;
    }

    [Theory]
    [InlineData(GiftKind.Money, ResourceKind.Money, 5000u)]
    [InlineData(GiftKind.Food, ResourceKind.Food, 50000u)]
    public void InvalidatedTargetReturnsPrepaidResourcesOnArrivalWithoutGiftEffects(GiftKind kind, ResourceKind resource, uint amount)
    {
        var session = CancelledGift(kind, out var content);
        Assert.Equal(new ReturnCargo(resource, amount), Assert.Single(session.Snapshot.Simulation.Tasks).ReturnCargo);
        session = SaveService.Decode(SaveService.Encode(session), content);
        var arrival = session.AdvanceXun();
        var refund = Assert.Single(arrival.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-returned");
        Assert.Equal((long)amount, refund.Actual);
        Assert.Equal(resource, Assert.Single(arrival.Events.OfType<ReturnCargoDeliveredEvent>()).Resource);
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        Assert.DoesNotContain(session.AdvanceXun().Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-returned");
    }

    [Fact]
    public void RedirectedReturnKeepsTheParcelAndSaturatesOnlyAtArrival()
    {
        var session = CancelledGift(GiftKind.Money, out var content);
        var state = session.Capture();
        var world = WorldState.Restore(state.World).BeginMutation();
        var old = Assert.Single(world.Simulation.Tasks);
        List<DomainEvent> redirect = [];
        TaskCancellation.Apply(world, old, "test-redirect", redirect, preserveCargo: true);
        PersonnelReturns.Start(world, old, session.View.Rules.Personnel, redirect, false, interrupted: true);
        var returning = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(old.ReturnCargo, returning.ReturnCargo);
        Assert.Empty(redirect.OfType<ReturnCargoLostEvent>());
        Assert.Empty(redirect.OfType<ReturnCargoDeliveredEvent>());
        Assert.Empty(redirect.OfType<PersonnelJourneyInterruptedEvent>());
        var root = world.Corps[1];
        world.Replace(root with { Treasury = root.Treasury with { Money = TreasuryRules.Capacity - 100 } });
        session = GameSession.Restore(content, state with { World = world.CapturePresentation() });
        var arrival = session.AdvanceXun();
        var delivered = Assert.Single(arrival.Events.OfType<ReturnCargoDeliveredEvent>());
        Assert.Equal((5000u, 100u), (delivered.Amount, delivered.Received));
        Assert.Equal(4900, Assert.Single(arrival.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "gift-returned").SaturationLoss);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedOrDeadCarrierDoesNotRefundTheParcel(bool capture)
    {
        var session = CancelledGift(GiftKind.Money, out _);
        var world = WorldState.Restore(session.Snapshot).BeginMutation();
        var task = Assert.Single(world.Simulation.Tasks);
        List<DomainEvent> events = [];
        if (capture) PersonnelTransitions.Capture(world, 12, 2, null, session.View.Rules.Personnel, events);
        else Assert.True(OfficerDeath.Remove(world, 12, OfficerDeathCause.Natural, session.View.Rules.Personnel, events, false));
        TaskCancellation.Apply(world, task, "duplicate-cleanup", events);
        Assert.Empty(world.Simulation.Tasks);
        Assert.Equal(5000u, Assert.Single(events.OfType<ReturnCargoLostEvent>()).Amount);
        Assert.Empty(events.OfType<ResourceChangedEvent>());
        Assert.Empty(events.OfType<ReturnCargoDeliveredEvent>());
    }

    [Fact]
    public void LosingTheLastHomeRecordsTheParcelLossWithoutRefunding()
    {
        var session = CancelledGift(GiftKind.Money, out _);
        var world = WorldState.Restore(session.Snapshot).BeginMutation();
        var task = Assert.Single(world.Simulation.Tasks);
        List<DomainEvent> events = [];
        world.Replace(world.Settlements[1] with { OwnerId = 2 });
        TaskCancellation.Apply(world, task, "test-home-lost", events, preserveCargo: true);
        PersonnelReturns.Start(world, task, session.View.Rules.Personnel, events, false, interrupted: true);
        Assert.Empty(world.Simulation.Tasks);
        Assert.Equal(5000u, Assert.Single(events.OfType<ReturnCargoLostEvent>()).Amount);
        Assert.Empty(events.OfType<ReturnCargoDeliveredEvent>());
        Assert.DoesNotContain(events.OfType<ResourceChangedEvent>(), e => e.Resource == ResourceKind.Money && e.Actual > 0);
    }
    [Theory]
    [InlineData(GiftKind.Money)]
    [InlineData(GiftKind.Food)]
    public void GiftReceiptPreviewPreservesPendingStateAndMatchesSaturatedSettlement(GiftKind kind)
    {
        var content = Content();
        content = content with { Information = content.Information with { Corps = content.Information.Corps
            .Select(c => c.OwnerId == 1 ? c with { Treasury = new(TreasuryRules.Capacity - 100, TreasuryRules.Capacity - 100) } : c).ToArray() } };
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        var command = new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1, new(kind, GiftOffer.MinimumAmount(kind), null));
        Assert.True(session.Execute(new("ai:2"), command).Submission.Accepted);
        session.AdvanceXun();
        var before = session.Snapshot;
        var preview = Assert.IsType<GiftReceiptPreview>(session.GetGiftReceiptPreview(command.CommandId));
        Assert.Equal(preview, session.GetGiftReceiptPreview(command.CommandId));
        StateAssert.Equal(before, session.Snapshot);
        Assert.Equal(TreasuryRules.Capacity, kind == GiftKind.Money ? preview.MoneyAfter : preview.FoodAfter);
        var pending = session.Snapshot.Simulation.Tasks.Single(t => t.Id == command.CommandId);
        var result = session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, pending.Id, pending.ResolutionStep, true));
        Assert.True(result.Submission.Accepted);
        var receipt = Assert.Single(result.Events.OfType<GiftResolvedEvent>());
        Assert.Equal(kind == GiftKind.Money ? preview.MoneyAfter - preview.MoneyBefore : preview.FoodAfter - preview.FoodBefore, receipt.Received);
        Assert.InRange(receipt.Received, 1u, 999u);
    }

}
