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
    private static SaveSlotSummary Summary(string note) => new("君主", null, "190年1月", "自创剧本", "190年1月上旬",
        DateTimeOffset.UnixEpoch, 1, null, "标准", note);

    [Fact]
    public void SummaryUsesAuthoredTitleInsteadOfInferringItFromTheYearOrScenarioId()
    {
        var definition = Definition;
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new() { ["player"] = "君主", [San9.Content.Text.TextCatalog.ScenarioTitleKey] = "自创剧本：群雄并起" });
        var summary = SaveSlotSummaryFactory.Create(GameSession.Start(definition, 1), definition, texts, "备注");
        Assert.Equal("自创剧本：群雄并起", summary.Scenario);
        Assert.Equal("190年 1月", summary.StartDate);
        Assert.Equal("190年 1月上旬", summary.CurrentDate);
    }

    [Fact]
    public void RestartedCatalogAndPlayerLoadRestoreTheSavedWorldAndUnicodeNoteTogether()
    {
        var definition = Definition;
        var session = GameSession.Start(definition, 1);
        var store = new SlotSaveStore(_directory, definition);
        Assert.Empty(store.ReadCatalog(43));
        Assert.False(Directory.Exists(_directory));
        var summary = Summary("汉字、emoji🙂和长备注" + new string('字', 100));
        store.Write(43, SlotSaveStore.Encode(session, summary));
        byte[] saved = File.ReadAllBytes(store.PathFor(43));
        session.AdvanceXun();
        var restarted = new SlotSaveStore(_directory, definition);
        var preview = restarted.ReadCatalog(43)[43];
        Assert.Equal(summary, preview.Summary);
        Assert.Equal(new SaveSlotFlag(session.View.Forces.Single().ColorCode, session.View.Forces.Single().FlagGlyph), preview.Flag);
        var restored = restarted.Load(43);
        Assert.Equal(new GameClock(190, 1, 1), restored.Snapshot.Clock);
        Assert.NotEqual(session.Snapshot.Simulation.RunSeed, restored.Snapshot.Simulation.RunSeed);
        Assert.Equal(100u, restored.View.PlayerCorps!.Treasury.Money);
        Assert.Equal(saved, File.ReadAllBytes(store.PathFor(43)));
    }

    [Fact]
    public void ManuallyEditedSummaryRemainsReadableAndLoadable()
    {
        var store = new SlotSaveStore(_directory, Definition);
        store.Write(1, SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("原备注")));
        var document = JsonNode.Parse(File.ReadAllBytes(store.PathFor(1)))!;
        document["header"]!["summary"]!["note"] = "手动修改备注";
        string edited = document.ToJsonString();
        File.WriteAllText(store.PathFor(1), edited, new UTF8Encoding(false));
        var entry = Assert.Single(store.ReadCatalog(40)).Value;
        Assert.Equal("手动修改备注", entry.Summary!.Note);
        Assert.Null(entry.Error);
        Assert.Equal(100u, store.Load(1).View.PlayerCorps!.Treasury.Money);
        Assert.Equal(edited, File.ReadAllText(store.PathFor(1), Encoding.UTF8));
    }

    [Fact]
    public void ChangedContentRemainsBrowsableAndLoadsSavedState()
    {
        var session = GameSession.Start(Definition, 1);
        var store = new SlotSaveStore(_directory, Definition);
        store.Write(2, SlotSaveStore.Encode(session, Summary("存档")));
        var other = new SlotSaveStore(_directory, Definition with { ScenarioId = "other" });
        var listing = other.ReadCatalog(40)[2];
        Assert.NotNull(listing.Summary);
        Assert.Null(listing.Error);
        Assert.Equal(session.Snapshot.Clock, other.Load(2).Snapshot.Clock);
        Assert.NotNull(store.ReadCatalog(40)[2].Summary);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("saveVersion")]
    public void UnsupportedHeaderVersionsAreRejectedWithoutReadingTheBody(string field)
    {
        var store = new SlotSaveStore(_directory, Definition);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("旧存档")))!;
        document["header"]![field] = -1;
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal(SaveFailure.UnsupportedVersion, store.ReadCatalog(40)[1].Error!.Failure);
        Assert.Equal(SaveFailure.UnsupportedVersion, Assert.Throws<SaveException>(() => store.Load(1)).Failure);
    }

    [Fact]
    public void OlderRulesRemainBrowsableAndLoadWithWarning()
    {
        var store = new SlotSaveStore(_directory, Definition);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("旧存档")))!;
        document["header"]!["rulesVersion"] = "older-rules";
        document["save"]!["rulesVersion"] = "older-rules";
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        var listing = store.ReadCatalog(40)[1];
        Assert.Null(listing.Error);
        Assert.Equal("旧存档", listing.Summary!.Note);
        Assert.Equal("older-rules", Assert.Single(store.Load(1).RulesVersionWarnings).SavedVersion);
    }

    [Fact]
    public void BodyCorruptionDoesNotRequireOpeningEverySaveDuringBrowsingButLoadRejectsIt()
    {
        var store = new SlotSaveStore(_directory, Definition);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("摘要")))!;
        document["save"]!["session"]!["world"]!.AsObject().Remove("simulation");
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal("摘要", store.ReadCatalog(40)[1].Summary!.Note);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => store.Load(1)).Failure);
    }

    [Fact]
    public void ManuallyEditedWorldLoadsWithoutRewritingThePreview()
    {
        var store = new SlotSaveStore(_directory, Definition);
        var document = JsonNode.Parse(SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("摘要")))!;
        document["save"]!["session"]!["world"]!["corps"]![0]!["treasury"]!["money"] = 321;
        store.Write(1, Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal("摘要", store.ReadCatalog(1)[1].Summary!.Note);
        Assert.Equal(321u, store.Load(1).View.PlayerCorps!.Treasury.Money);
    }

    [Fact]
    public void PreviewReadsOnlyTheHeaderEvenWithALargeUnreadableBody()
    {
        var store = new SlotSaveStore(_directory, Definition);
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
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => store.Load(1)).Failure);
    }

    [Fact]
    public void FailedAtomicReplacementLeavesTheDestinationAndCleansTemporaryFiles()
    {
        var store = new SlotSaveStore(_directory, Definition);
        Directory.CreateDirectory(store.PathFor(1));
        Assert.Equal(SaveFailure.IoFailure, Assert.Throws<SaveException>(() =>
            store.Write(1, SlotSaveStore.Encode(GameSession.Start(Definition, 1), Summary("无法写入")))).Failure);
        Assert.True(Directory.Exists(store.PathFor(1)));
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Equal(SaveFailure.IoFailure, store.ReadCatalog(40)[1].Error!.Failure);
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
