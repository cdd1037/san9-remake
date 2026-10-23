using System.Text;
using System.Text.Json.Nodes;
using San9.Application;
using San9.Application.Persistence;
using San9.Client.Godot.Persistence;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class SlotSaveStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-slot-tests", Guid.NewGuid().ToString("N"));
    private static ScenarioInitialState Definition => new(ScenarioInitialState.CurrentSchemaVersion, "slot-test", 190, 1, 1, 5,
        [new(5, "player", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
    { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
    private static SaveSlotSummary Summary(string note) => new("slot-test", "君主", null, "190年1月", "自创剧本", "190年1月上旬",
        DateTimeOffset.UnixEpoch, 1, null, "标准", note);

    [Fact]
    public void CurrentDirectorySlotCanBeWrittenAndReplaced()
    {
        var store = new SlotSaveStore("");
        int slot = Random.Shared.Next(100000, int.MaxValue);
        string path = store.PathFor(slot);
        try
        {
            var session = GameSession.Start(Definition, 1);
            store.Write(slot, SlotSaveStore.Encode(session, Summary("first")));
            session.AdvanceXun();
            store.Write(slot, SlotSaveStore.Encode(session, Summary("replacement")));
            Assert.Equal(session.Snapshot.Clock, store.Load(slot, Definition).Snapshot.Clock);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SummaryUsesAuthoredTitleInsteadOfInferringItFromTheYearOrScenarioId()
    {
        var definition = Definition;
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new() { ["player"] = "君主", [San9.Content.Text.TextCatalog.ScenarioTitleKey] = "自创剧本：群雄并起" });
        var summary = SaveSlotSummaryFactory.Create(GameSession.Start(definition, 1), definition, texts, new Dictionary<string, string>(), "备注");
        Assert.Equal("自创剧本：群雄并起", summary.Scenario);
        Assert.Equal(definition.ScenarioId, summary.ScenarioId);
        Assert.Equal("190年 1月", summary.StartDate);
        Assert.Equal("190年 1月上旬", summary.CurrentDate);
    }

    [Fact]
    public async Task PreviewPreservesTheSharedPortraitAssetIndependentlyOfScenarioPersonRoles()
    {
        var definition = Definition with
        {
            Forces = [Definition.Forces[0] with { LeaderId = 7 }],
            Information = Definition.Information with { Corps = [Definition.Information.Corps[0] with { LeaderId = 7 }] },
            Officers = [new("ruler", new San9.Core.Domain.OfficerState(7, 5, 15, new(50, 50, 50, 50), false))]
        };
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new() { ["ruler"] = "君主", ["player"] = "势力", [San9.Content.Text.TextCatalog.ScenarioTitleKey] = "剧本" });
        var session = GameSession.Start(definition, 1, enableXunStartCheckpoints: true);
        var roles = new Dictionary<string, string> { ["information-person-7"] = "portraits/author-a" };
        var summary = SaveSlotSummaryFactory.Create(session, definition, texts, roles, "手动档");
        var store = new SlotSaveStore(_directory);
        store.Write(1, SlotSaveStore.Encode(session, summary));
        await new AutoSaveQueue(store).Enqueue(session,
            view => SaveSlotSummaryFactory.Create(view, definition, texts, roles, "自动档"));
        roles["information-person-7"] = "portraits/author-b";
        var other = SaveSlotSummaryFactory.Create(session, definition with { ScenarioId = "other" }, texts, roles, "另一剧本");
        store.Write(2, SlotSaveStore.Encode(session, other));

        var restarted = new SlotSaveStore(_directory);
        Assert.Equal("portraits/author-a", restarted.ReadCatalog(2)[1].Summary!.PortraitAssetId);
        Assert.Equal("portraits/author-b", restarted.ReadCatalog(2)[2].Summary!.PortraitAssetId);
        Assert.Equal("portraits/author-a", Assert.Single(restarted.ReadAutoSaveCatalog()).Listing.Summary!.PortraitAssetId);
        Assert.Null(SaveSlotSummaryFactory.Create(GameSession.Start(Definition, 1), Definition, texts, roles, "无君主").PortraitAssetId);
    }

    [Fact]
    public void RestartedCatalogAndPlayerLoadRestoreTheSavedWorldAndUnicodeNoteTogether()
    {
        var definition = Definition;
        var session = GameSession.Start(definition, 1);
        var store = new SlotSaveStore(_directory);
        Assert.Empty(store.ReadCatalog(43));
        Assert.False(Directory.Exists(_directory));
        var summary = Summary("汉字、emoji🙂和长备注" + new string('字', 100));
        store.Write(43, SlotSaveStore.Encode(session, summary));
        byte[] saved = File.ReadAllBytes(store.PathFor(43));
        session.AdvanceXun();
        var restarted = new SlotSaveStore(_directory);
        var preview = restarted.ReadCatalog(43)[43];
        Assert.Equal(summary, preview.Summary);
        Assert.Equal(new SaveSlotFlag(session.View.Forces.Single().ColorCode, session.View.Forces.Single().FlagGlyph), preview.Flag);
        var restored = restarted.Load(43, Definition);
        Assert.Equal(new GameClock(190, 1, 1), restored.Snapshot.Clock);
        Assert.NotEqual(session.Snapshot.Simulation.RunSeed, restored.Snapshot.Simulation.RunSeed);
        Assert.Equal(100u, restored.View.PlayerCorps!.Treasury.Money);
        Assert.Equal(saved, File.ReadAllBytes(store.PathFor(43)));
    }

    [Fact]
    public void ManuallyEditedSummaryRemainsReadableAndLoadable()
    {
        var store = new SlotSaveStore(_directory);
        store.Write(1, SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("原备注")));
        var document = JsonNode.Parse(File.ReadAllBytes(store.PathFor(1)))!;
        document["header"]!["summary"]!["note"] = "手动修改备注";
        string edited = document.ToJsonString();
        File.WriteAllText(store.PathFor(1), edited, new UTF8Encoding(false));
        var entry = Assert.Single(store.ReadCatalog(40)).Value;
        Assert.Equal("手动修改备注", entry.Summary!.Note);
        Assert.Null(entry.Error);
        Assert.Equal(100u, store.Load(1, Definition).View.PlayerCorps!.Treasury.Money);
        Assert.Equal(edited, File.ReadAllText(store.PathFor(1), Encoding.UTF8));
    }

    [Fact]
    public void ChangedContentOfTheSameScenarioRemainsBrowsableAndLoadsSavedState()
    {
        var session = GameSession.Start(Definition, 1);
        var store = new SlotSaveStore(_directory);
        store.Write(2, SlotSaveStore.Encode(session, Summary("存档")));
        var other = new SlotSaveStore(_directory);
        var listing = other.ReadCatalog(40)[2];
        Assert.NotNull(listing.Summary);
        Assert.Null(listing.Error);
        var changed = Definition with { Forces = [Definition.Forces[0] with { Trust = 200 }] };
        Assert.Equal(session.Snapshot.Clock, other.Load(2, changed).Snapshot.Clock);
        Assert.NotNull(store.ReadCatalog(40)[2].Summary);
    }

    [Fact]
    public async Task OpeningCanResolveManualAndAutomaticDocumentsWithoutAnActiveScenario()
    {
        var definition = Definition with { ScenarioId = "mod:another-scenario", Year = 207 };
        var session = GameSession.Start(definition, 1, enableXunStartCheckpoints: true);
        var summary = Summary("开局读档") with { ScenarioId = definition.ScenarioId };
        var store = new SlotSaveStore(_directory);
        store.Write(2, SlotSaveStore.Encode(session, summary));
        var checkpoint = session.TakeXunStartCheckpoint()!;
        await store.WriteAutoSaveAsync(SlotSaveStore.CaptureAutoSave(checkpoint, session.View, _ => summary));
        session.AddChronicleNote("原会话继续游玩");
        var restarted = new SlotSaveStore(_directory);
        foreach (var document in new[] { restarted.ReadDocument(2), restarted.ReadAutoSaveDocument(1) })
        {
            Assert.Equal(definition.ScenarioId, document.Header.Summary.ScenarioId);
            var loaded = SlotSaveStore.Restore(document, definition);
            Assert.Empty(loaded.Chronicle);
            Assert.Equal(new GameClock(207, 1, 1), loaded.Snapshot.Clock);
            loaded.EnableXunStartCheckpoints();
            Assert.Null(loaded.TakeXunStartCheckpoint());
            var error = Assert.Throws<SaveException>(() => SlotSaveStore.Restore(document, Definition));
            Assert.Equal(SaveFailure.InvalidState, error.Failure);
        }
        Assert.Throws<System.Text.Json.JsonException>(() => SlotSaveStore.Encode(session, summary with { ScenarioId = "" }));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("saveVersion")]
    public void UnsupportedHeaderVersionsAreRejectedWithoutReadingTheBody(string field)
    {
        var store = new SlotSaveStore(_directory);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("旧存档")))!;
        document["header"]![field] = -1;
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal(SaveFailure.UnsupportedVersion, store.ReadCatalog(40)[1].Error!.Failure);
        Assert.Equal(SaveFailure.UnsupportedVersion, Assert.Throws<SaveException>(() => store.Load(1, Definition)).Failure);
    }

    [Fact]
    public void OlderRulesRemainBrowsableAndLoadWithWarning()
    {
        var store = new SlotSaveStore(_directory);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("旧存档")))!;
        document["header"]!["rulesVersion"] = "older-rules";
        document["save"]!["rulesVersion"] = "older-rules";
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        var listing = store.ReadCatalog(40)[1];
        Assert.Null(listing.Error);
        Assert.Equal("旧存档", listing.Summary!.Note);
        Assert.Equal("older-rules", Assert.Single(store.Load(1, Definition).RulesVersionWarnings).SavedVersion);
    }

    [Fact]
    public void BodyCorruptionDoesNotRequireOpeningEverySaveDuringBrowsingButLoadRejectsIt()
    {
        var store = new SlotSaveStore(_directory);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("摘要")))!;
        document["save"]!["session"]!["world"]!.AsObject().Remove("simulation");
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal("摘要", store.ReadCatalog(40)[1].Summary!.Note);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => store.Load(1, Definition)).Failure);
    }

    [Fact]
    public void ManuallyEditedWorldLoadsWithoutRewritingThePreview()
    {
        var store = new SlotSaveStore(_directory);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("摘要")))!;
        document["save"]!["session"]!["world"]!["corps"]![0]!["treasury"]!["money"] = 321;
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal("摘要", store.ReadCatalog(1)[1].Summary!.Note);
        Assert.Equal(321u, store.Load(1, Definition).View.PlayerCorps!.Treasury.Money);
    }

    [Fact]
    public void PreviewReadsOnlyTheHeaderEvenWithALargeUnreadableBody()
    {
        var store = new SlotSaveStore(_directory);
        var bytes = SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary(new string('字', 2000)));
        int body = bytes.AsSpan().IndexOf(",\"save\":"u8);
        Assert.True(body > 0);
        byte[] large = new byte[8 * 1024 * 1024];
        bytes.AsSpan(0, body).CopyTo(large);
        ",\"save\":"u8.CopyTo(large.AsSpan(body));
        using var stream = new MemoryStream(large);
        var header = store.ReadPreview(stream);
        Assert.Equal(new string('字', 2000), header.Summary.Note);
        Assert.InRange(stream.Position, body, 2 * body + 1024);
        store.Write(1, large);
        Assert.NotNull(store.ReadCatalog(40)[1].Summary);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => store.Load(1, Definition)).Failure);
    }

    [Fact]
    public void OversizedPreviewIsBoundedAndDoesNotHideOtherSlots()
    {
        var store = new SlotSaveStore(_directory);
        var oversized = Encoding.UTF8.GetBytes("{\"header\":{\"unfinished\":\"" + new string('x', SlotSaveStore.MaximumPreviewBytes * 2));
        using var stream = new MemoryStream(oversized);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => store.ReadPreview(stream)).Failure);
        Assert.Equal(SlotSaveStore.MaximumPreviewBytes, stream.Position);
        store.Write(1, oversized);
        store.Write(2, SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("valid")));
        var catalog = store.ReadCatalog(2);
        Assert.Equal(SaveFailure.InvalidFormat, catalog[1].Error!.Failure);
        Assert.Equal("valid", catalog[2].Summary!.Note);
        Assert.Throws<SaveException>(() => SlotSaveStore.Encode(GameSession.Start(Definition, 1),
            Summary(new string('x', SlotSaveStore.MaximumPreviewBytes))));
    }

    [Fact]
    public void FailedAtomicReplacementLeavesTheDestinationAndCleansTemporaryFiles()
    {
        var store = new SlotSaveStore(_directory);
        Directory.CreateDirectory(store.PathFor(1));
        Assert.Equal(SaveFailure.IoFailure, Assert.Throws<SaveException>(() =>
            store.Write(1, SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("无法写入")))).Failure);
        Assert.True(Directory.Exists(store.PathFor(1)));
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Equal(SaveFailure.IoFailure, store.ReadCatalog(40)[1].Error!.Failure);
    }
    [Fact]
    public async Task AutoSaveCapturesBeforeOrdersAndDoesNotOccupyOrReplaceManualSlots()
    {
        var store = new SlotSaveStore(_directory);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        byte[] manual = SlotSaveStore.Encode(session, Summary("手动档"));
        store.Write(1, manual);
        var queue = new AutoSaveQueue(store);
        var view = session.View;
        var write = queue.Enqueue(session, captured =>
        {
            Assert.Same(view, captured);
            return Summary("自动档");
        });
        session.AdvanceXun();
        long? sequence = await write;
        session.MarkSaved(sequence!.Value);
        Assert.Equal(new GameClock(190, 1, 1), store.LoadAutoSave(Assert.Single(store.ReadAutoSaveCatalog()).Slot, Definition).Snapshot.Clock);
        Assert.Equal(manual, File.ReadAllBytes(store.PathFor(1)));
        Assert.Single(store.ReadCatalog(40));
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task DelayedAutoSaveProjectsTheCheckpointUsingSharedDefinitions()
    {
        var store = new SlotSaveStore(_directory);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        var initial = session.View;
        Assert.Equal(SessionDisposition.Accepted,
            session.Apply(session.Request(new PkSettingsOperation(false, false, true))).Disposition);
        session.AddChronicleNote("领取前修改");
        var current = session.View;
        Assert.NotSame(initial.World, current.World);
        await new AutoSaveQueue(store).Enqueue(session, captured =>
        {
            Assert.Same(initial.World, captured.World);
            Assert.Same(current.Information, captured.Information);
            Assert.Same(current, session.View);
            return Summary("延迟保存");
        });
        var loaded = store.LoadAutoSave(1, Definition);
        Assert.Empty(loaded.Chronicle);
        Assert.False(loaded.Snapshot.Simulation.BattleDeathsEnabled);
        Assert.True(session.Snapshot.Simulation.BattleDeathsEnabled);
        Assert.Single(session.Chronicle);
    }

    [Fact]
    public async Task OneCheckpointIsClaimedOnceAcrossQueuesAndInterfaceReconstruction()
    {
        var store = new SlotSaveStore(_directory);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        var first = new AutoSaveQueue(store).Enqueue(session, _ => Summary("首次"));
        Assert.Null(await new AutoSaveQueue(store).Enqueue(session, _ => throw new Exception("Duplicate capture")));
        await first;
        session.AddChronicleNote("本旬下令后");
        Assert.Null(await new AutoSaveQueue(store).Enqueue(session, _ => throw new Exception("UI rebind is not a new xun")));
        var entry = Assert.Single(store.ReadAutoSaveCatalog());
        Assert.Equal("首次", entry.Listing.Summary!.Note);
        Assert.Empty(store.LoadAutoSave(entry.Slot, Definition).Chronicle);
    }

    [Fact]
    public async Task QueuedXunSavesFinishInRequestOrderWithTheirOwnCapturedState()
    {
        var store = new SlotSaveStore(_directory);
        var queue = new AutoSaveQueue(store);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        List<Task> writes = [];
        for (int i = 0; i < 12; i++)
        {
            int xun = i;
            writes.Add(queue.Enqueue(session, _ => Summary($"旬{xun}") with { SavedAt = DateTimeOffset.UnixEpoch.AddSeconds(xun) }));
            session.AdvanceXun();
        }
        await Task.WhenAll(writes);
        var entries = new SlotSaveStore(_directory).ReadAutoSaveCatalog();
        Assert.Equal(new[] { 2, 1, 10, 9, 8, 7, 6, 5, 4, 3 }, entries.Select(e => e.Slot));
        Assert.Equal(Enumerable.Range(2, 10).Reverse().Select(i => $"旬{i}"), entries.Select(e => e.Listing.Summary!.Note));
        Assert.Equal(10, Directory.GetFiles(_directory, "auto-*.save.json").Length);
        Assert.Equal(110, store.LoadAutoSave(entries[0].Slot, Definition).Snapshot.Clock.DayNumber - new GameClock(190, 1, 1).DayNumber);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 11)]
    [InlineData(12, 21)]
    public async Task RotationUsesElapsedXunFromTheScenarioStart(int month, int day)
    {
        var definition = Definition with { Month = month, Day = day };
        var store = new SlotSaveStore(_directory);
        var queue = new AutoSaveQueue(store);
        var session = GameSession.Start(definition, 1, enableXunStartCheckpoints: true);
        for (int i = 0; i < 11; i++)
        {
            await queue.Enqueue(session, _ => Summary("轮换"));
            Assert.True(File.Exists(store.AutoSavePathFor(i % 10 + 1)));
            session.AdvanceXun();
        }
        Assert.Equal(10, store.ReadAutoSaveCatalog().Count);
        Assert.Equal(100, store.LoadAutoSave(store.ReadAutoSaveCatalog().Single(e => e.Slot == 1).Slot, definition).Snapshot.Clock.DayNumber -
            new GameClock(190, month, day).DayNumber);
    }

    [Fact]
    public async Task LoadingDoesNotSaveAndReplayingAnOldXunReplacesItsDatePosition()
    {
        var store = new SlotSaveStore(_directory);
        var queue = new AutoSaveQueue(store);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        await queue.Enqueue(session, _ => Summary("首旬"));
        var first = Assert.Single(store.ReadAutoSaveCatalog()).Slot;
        session.AdvanceXun();
        await queue.Enqueue(session, _ => Summary("原第二旬"));
        var original = store.ReadAutoSaveCatalog().Single(e => e.Slot == 2).Slot;
        var loaded = store.LoadAutoSave(first, Definition);
        Assert.Null(await queue.Enqueue(loaded, _ => throw new Exception("Loading must not capture")));
        Assert.Equal(2, store.ReadAutoSaveCatalog().Count);
        loaded.EnableXunStartCheckpoints();
        Assert.Null(await queue.Enqueue(loaded, _ => throw new Exception("Enabling after load must not capture the loaded xun")));
        loaded.AdvanceXun();
        await queue.Enqueue(loaded, _ => Summary("重走第二旬"));
        var replacement = store.ReadAutoSaveCatalog().Single(e => e.Slot == 2);
        Assert.Equal(2, store.ReadAutoSaveCatalog().Count);
        Assert.Equal(original, replacement.Slot);
        Assert.Equal("重走第二旬", replacement.Listing.Summary!.Note);
        Assert.Equal(loaded.Snapshot.Clock, store.LoadAutoSave(original, Definition).Snapshot.Clock);
    }

    [Fact]
    public async Task FailedAutoSavePreservesHistoryAndCanRetryTheSameCheckpoint()
    {
        var store = new SlotSaveStore(_directory);
        var queue = new AutoSaveQueue(store);
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        for (int i = 0; i < 4; i++)
        {
            await queue.Enqueue(session, _ => Summary("已有"));
            session.AdvanceXun();
        }
        var files = Directory.GetFiles(_directory).ToDictionary(path => path, File.ReadAllBytes);
        Directory.CreateDirectory(store.AutoSavePathFor(5));
        var error = await Assert.ThrowsAsync<SaveException>(() => queue.Enqueue(session, _ => Summary("写入失败")));
        Assert.Equal(SaveFailure.IoFailure, error.Failure);
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(session.HasUnsavedChanges);
        Directory.Delete(store.AutoSavePathFor(5));
        Assert.NotNull(await queue.Enqueue(session, _ => Summary("重试")));
        Assert.Equal(5, store.ReadAutoSaveCatalog().Count);
        Assert.Equal("重试", store.ReadAutoSaveCatalog().Single(e => e.Slot == 5).Listing.Summary!.Note);
    }

    [Fact]
    public async Task ManualBackgroundSaveEncodesTheCapturedStateAndOnlyClearsItsOwnSequence()
    {
        var session = GameSession.Start(Definition, 1, enableXunStartCheckpoints: true);
        var store = new SlotSaveStore(_directory);
        var document = SlotSaveStore.Capture(session, Summary("手动异步"));
        session.AddChronicleNote("捕获后的修改");
        await store.WriteAsync(1, document);
        session.MarkSaved(document.Save.Session.NextOperationSequence);
        Assert.True(session.HasUnsavedChanges);
        Assert.Empty(store.Load(1, Definition).Chronicle);
        Assert.Equal("手动异步", store.ReadCatalog(40)[1].Summary!.Note);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
