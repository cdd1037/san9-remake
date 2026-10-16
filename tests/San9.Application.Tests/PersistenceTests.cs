using San9.Application.Diagnostics;
using San9.Tests;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersistenceTests : IDisposable
{
    [Fact]
    public void UnsavedChangesTrackCommittedOperationsButNotQueriesOrRejectedRequests()
    {
        var session = GameSession.Start(Fixture(), 190);
        Assert.True(session.HasUnsavedChanges);
        var before = session.Capture();
        SaveService.Write(FilePath("dirty.json"), session);
        Assert.False(session.HasUnsavedChanges);
        Assert.Same(before, session.Capture());
        _ = session.View;
        session.BeginRecording();
        session.EndRecording();
        var rejected = session.Apply(session.Request(new ChronicleOperation("rejected"), "ai:2"));
        Assert.Equal(SessionDisposition.Rejected, rejected.Disposition);
        Assert.False(session.HasUnsavedChanges);
        var request = session.Request(new ChronicleOperation("changed"));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        Assert.True(session.HasUnsavedChanges);
        SaveService.Write(FilePath("dirty.json"), session);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.False(session.HasUnsavedChanges);
        session.AdvanceXun();
        Assert.True(session.HasUnsavedChanges);
        var loaded = SaveService.Load(FilePath("dirty.json"), Fixture());
        Assert.False(loaded.HasUnsavedChanges);
        Assert.True(session.HasUnsavedChanges);
        Assert.DoesNotContain("hasUnsavedChanges", Encoding.UTF8.GetString(SaveService.Encode(session)));
    }

    [Fact]
    public void CompletingAnOlderSaveDoesNotClearNewerChanges()
    {
        var session = GameSession.Start(Fixture(), 190);
        SaveService.Write(FilePath("older.json"), session, () => session.AddChronicleNote("changed during write"));
        Assert.True(session.HasUnsavedChanges);
        Assert.Empty(SaveService.Read(FilePath("older.json"), Fixture()).Chronicle);
        SaveService.Write(FilePath("older.json"), session);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public void ConsumedEventKeysSurviveSaveAndRecording()
    {
        var definition = Fixture() with { ConsumedEventKeys = [new("history")] };
        var session = GameSession.Start(definition, 190);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal("history", Assert.Single(restored.Snapshot.Simulation.ConsumedEventKeys).Value);
        restored.BeginRecording();
        restored.AdvanceXun();
        var replay = RecordingService.Replay(RecordingService.Encode(restored.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(restored.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void InitialEventConsumptionIsValidatedAndInputOrderDoesNotChangeWorld()
    {
        var basis = Fixture();
        var definition = basis with { ConsumedEventKeys = [new("z"), new("a")] };
        var session = GameSession.Start(definition, 190);
        var reordered = GameSession.Start(definition with { ConsumedEventKeys = [new("a"), new("z")] }, 190);

        StateAssert.Equal(session.Capture(), reordered.Capture());
        Assert.Equal(["a", "z"], session.Snapshot.Simulation.ConsumedEventKeys.Select(k => k.Value));

        Assert.Throws<InvalidDataException>(() => (basis with { ConsumedEventKeys = [default] }).Validate());
        Assert.Throws<InvalidDataException>(() => (basis with { ConsumedEventKeys = [new("a"), new("a")] }).Validate());
    }

    [Fact]
    public void InitialAllianceSurvivesSaveAndRecordedExpiryWithoutRepeatingInitialUpdates()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = basis.Forces.Append(new(3, "target", 3, 0)).ToArray(),
            Information = basis.Information with { Corps = [..basis.Information.Corps, new(3, 3, null, new(0, 0)) { IsRoot = true }] },
            Alliance = new(3, 1, [1, 2], 2)
        };
        var session = GameSession.Start(definition, 190);
        Assert.Equal(2, session.Snapshot.Simulation.Alliance!.RemainingXun);
        session.AdvanceXun();
        var restored = SaveService.Decode(SaveService.Encode(session), definition, GameSession.EngineFactory(definition));
        Assert.Equal(1, restored.Snapshot.Simulation.Alliance!.RemainingXun);
        Assert.Empty(restored.InitialEvents);
        session.BeginRecording();
        var expected = session.AdvanceXun();
        var actual = restored.AdvanceXun();
        Assert.Null(restored.Snapshot.Simulation.Alliance);
        StateAssert.Equal(session.Capture(), restored.Capture());
        EventAssert.Equal(expected.Events, actual.Events);
        var replayed = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replayed.Session.Capture());
        var missing = JsonNode.Parse(SaveService.Encode(session))!;
        missing["session"]!["world"]!["simulation"]!.AsObject().Remove("alliance");
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            Encoding.UTF8.GetBytes(missing.ToJsonString()), definition, GameSession.EngineFactory(definition))).Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRunSeedIsRejectedBeforeExactOrPlayerRestoration(bool playerLoad)
    {
        var definition = Fixture();
        var original = GameSession.Start(definition, 190);
        var root = JsonNode.Parse(SaveService.Encode(original))!;
        root["session"]!["world"]!["simulation"]!.AsObject().Remove("runSeed");
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(
            Encoding.UTF8.GetBytes(root.ToJsonString()), definition, GameSession.EngineFactory(definition), playerLoad)).Failure);
        Assert.Equal(190UL, original.Snapshot.Simulation.RunSeed);
    }

    [Fact]
    public void PlayerLoadStartsNewStreamsAndItsRecordingReplaysExactly()
    {
        var definition = Fixture();
        var rules = new FixtureRules();
        Func<WorldState, SimulationEngine> factory = w => new(w, rules);
        var original = GameSession.Start(definition, 190, factory);
        original.Apply(original.Request(new CommandOperation(new StartTaskCommand(Id(1), 1, 0,
            "fixture-investment", 2, 1)), id: Id(1)));
        original.AdvanceXun();
        byte[] save = SaveService.Encode(original);
        var loaded = SaveService.Decode(save, definition, factory, playerLoad: true);
        var again = SaveService.Decode(save, definition, factory, playerLoad: true);
        Assert.NotEqual(original.Snapshot.Simulation.RunSeed, loaded.Snapshot.Simulation.RunSeed);
        Assert.NotEqual(loaded.Snapshot.Simulation.RunSeed, again.Snapshot.Simulation.RunSeed);
        Assert.Equal(original.Snapshot.Simulation.InitialSeed, loaded.Snapshot.Simulation.InitialSeed);
        Assert.Equal(original.Snapshot.Simulation.Tasks.ToArray(), loaded.Snapshot.Simulation.Tasks.ToArray());
        Assert.Equal(CanonicalJson.Text(original.Snapshot.Forces), CanonicalJson.Text(loaded.Snapshot.Forces));
        Assert.Equal(original.NextOperationSequence, loaded.NextOperationSequence);
        Assert.Empty(loaded.Snapshot.Simulation.RandomStreams);
        Assert.Empty(loaded.InitialEvents);
        Assert.False(loaded.IsRecording);
        loaded.BeginRecording();
        loaded.AdvanceXun();
        Assert.NotEmpty(loaded.Snapshot.Simulation.RandomStreams);
        var recording = loaded.EndRecording();
        Assert.Equal(loaded.Snapshot.Simulation.RunSeed, recording.Initial.World.Simulation.RunSeed);
        var replay = RecordingService.Replay(RecordingService.Encode(recording), definition, factory);
        Assert.Equal(SaveService.Encode(loaded), SaveService.Encode(replay.Session));
        var exact = SaveService.Decode(SaveService.Encode(loaded), definition, factory);
        Assert.Equal(SaveService.Encode(loaded), SaveService.Encode(exact));
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-g02-" + Guid.NewGuid().ToString("N"));
    public PersistenceTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(_directory, recursive: true);
    }
    private string FilePath(string name) => Path.Combine(_directory, name);
    private static Guid Id(int value) => SimulationIdentity.Command(value, 0);

    private static ScenarioInitialState Fixture()
    {
        var value = G01ScenarioTests.Fixture();
        return value with
        {
            Units = [new(1, 1, San9.Core.Domain.MapCoordinates.Center(new(3, 4)), 100)],
            Information = new([], [new(1, "office", 1000, 20)], [new(1, "treasure", 0, 10, 5, null)],
                [new(1, 1, 1, new(1000, 2000)) { IsRoot = true }, new(2, 2, 4, new(1000, 2000)) { IsRoot = true }], [new(1, 2, TreasureDiscovery.Discovered, null)])
                { Relations = [new(1, 2, 50)] }
        };
    }

    private sealed class FixtureRules : SimulationRules
    {
        internal FixtureRules() { Treasures = new([new(1, default, 5)]); }
        internal int InitialUpdates { get; private set; }
        internal bool Fail { get; set; }
        internal Action? DuringDailyUpdate { get; set; }
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.BeforeDailyCommands) DuringDailyUpdate?.Invoke();
            if (stage == ResolutionStage.XunUpdate && world.Clock == world.Simulation.Started) InitialUpdates++;
        }
        internal override TaskRule? FindTask(string kind) => kind == "fixture-investment" ? new Investment(this) : null;
        private sealed class Investment(FixtureRules owner) : TaskRule
        {
            internal override int DurationDays(PersistentTask task) => 15;
            internal override bool Validate(WorldMutation world, PersistentTask task) => ResourceAccounts.Root(world, task.ForceId).Treasury.Money >= 50;
            internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
            {
                ResourceAccounts.Change(world, ResourceAccounts.Resolve(world, task.ForceId, null), ResourceKind.Money, -50, "fixture-investment", null, events, true);
                int increment = 1 + (int)(world.NextRandom("fixture-investment") % 10);
                world.Replace(world.Settlements[task.OriginSettlementId] with { Commerce = world.Settlements[task.OriginSettlementId].Commerce + increment });
                if (owner.Fail) throw new InvalidOperationException("Injected task failure after effects.");
            }
        }
    }

    [Fact]
    public void DiskRoundTripKeepsRecordsChronicleControlAndIdentityWithoutInitialUpdate()
    {
        var definition = Fixture();
        var rules = new FixtureRules();
        var session = GameSession.Start(definition, 190, w => new(w, rules));
        Assert.True(session.Develop(new(1, 1, [2], DomesticActivity.Commerce)).Accepted);
        session.AddChronicleNote("  已完成第一笔内政  ");
        session.EndControl(new("player"));
        string path = FilePath("save.json");
        SaveService.Write(path, session);
        var restored = SaveService.Read(path, definition, w => new(w, rules));
        Assert.Equal(1, rules.InitialUpdates);
        Assert.Empty(restored.InitialEvents);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(session.Records, restored.Records);
        Assert.Equal("已完成第一笔内政", Assert.Single(restored.Chronicle).Text);
        Assert.Equal(1, restored.Snapshot.Simulation.ActiveControl);
        Assert.Equal(session.Snapshot.Units, restored.Snapshot.Units);
        Assert.Equal(session.Snapshot.Treasures, restored.Snapshot.Treasures);
        Assert.Equal(session.Snapshot.Corps, restored.Snapshot.Corps);
        Assert.Equal(session.Snapshot.Relations, restored.Snapshot.Relations);
        Assert.Equal(session.View.Year, restored.View.Year);
        var request = session.Request(new CommandOperation(new DevelopCommand(Id(10), 2, 1,
            new(2, 2, [5], DomesticActivity.Cultivation))), "ai:2", Id(10));
        Equal(session.Apply(request), restored.Apply(request));
        StateAssert.Equal(session.Capture(), restored.Capture());
    }

    [Fact]
    public void ThirtySixXunLogWithPersistentTaskIsIdenticalAcrossDiskCheckpoints()
    {
        var definition = Fixture();
        var rules = new FixtureRules();
        Func<WorldState, SimulationEngine> factory = w => new(w, rules);
        var continuous = GameSession.Start(definition, 190, factory);
        continuous.BeginRecording();
        var resumed = GameSession.Start(definition, 190, factory);
        void Apply(SessionRequest request) => Equal(continuous.Apply(request), resumed.Apply(request));
        void Checkpoint()
        {
            SaveService.Write(FilePath("checkpoint.json"), resumed);
            resumed = SaveService.Read(FilePath("checkpoint.json"), definition, factory);
            StateAssert.Equal(continuous.Capture(), resumed.Capture());
        }
        Apply(continuous.Request(new ChronicleOperation("首轮记录")));
        Apply(continuous.Request(new CommandOperation(new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1)), id: Id(1)));
        Apply(continuous.Request(new CommandOperation(new DevelopCommand(Id(2), 1, 1, new(1, 1, [3], DomesticActivity.Commerce))), id: Id(2)));
        Checkpoint();
        for (int xun = 0; xun < 36; xun++)
        {
            if (xun == 10)
                Apply(continuous.Request(new CommandOperation(new StartTaskCommand(Id(50), 1,
                    continuous.Snapshot.Simulation.NextSequence, "fixture-investment", 2, 1)), id: Id(50)));
            foreach (var seat in continuous.Snapshot.Simulation.Controls)
            {
                long sequence = continuous.Snapshot.Simulation.NextSequence;
                Guid id = Id(100 + xun * 2 + seat.ForceId);
                var development = new DevelopCommand(id, seat.ForceId, sequence,
                    new(seat.ForceId, seat.ForceId, [seat.ForceId == 1 ? 3 : 5], DomesticActivity.Cultivation));
                GameCommand command = continuous.Validate(new(seat.ControllerId), development).Accepted
                    ? development : new WaitCommand(id, seat.ForceId, sequence);
                Apply(continuous.Request(new CommandOperation(command), seat.ControllerId, id));
                Apply(continuous.Request(new EndControlOperation(), seat.ControllerId));
            }
            Apply(continuous.Request(new AdvanceOperation()));
            if (xun == 0)
            {
                Assert.Single(resumed.Snapshot.Simulation.Tasks);
                Assert.Equal(10, resumed.Snapshot.Simulation.Tasks[0].ProgressDays);
                Checkpoint();
            }
            if (xun == 34) { Assert.Equal(new GameClock(190, 12, 21), resumed.Snapshot.Clock); Checkpoint(); }
            if (xun == 10)
            {
                Assert.Single(resumed.Snapshot.Simulation.Tasks);
                Assert.Single(resumed.Snapshot.Simulation.RandomStreams, s => s.Name == "fixture-investment");
                Checkpoint();
            }
        }
        Assert.Equal(new GameClock(191, 1, 1), resumed.Snapshot.Clock);
        Assert.Empty(resumed.Snapshot.Simulation.Tasks);
        Assert.Single(resumed.Snapshot.Simulation.RandomStreams, s => s.Name == "fixture-investment");
        StateAssert.Equal(continuous.Capture(), resumed.Capture());
        Assert.Equal(continuous.Records, resumed.Records);
        CommandLogService.Write(FilePath("commands.jsonl"), continuous);
        var replayed = GameSession.Start(definition, 190, factory);
        var replay = CommandLogService.Replay(FilePath("commands.jsonl"), replayed);
        Assert.Null(replay.FirstFailure);
        StateAssert.Equal(continuous.Capture(), replayed.Capture());
        Assert.Equal(continuous.CaptureRecording().Operations.SelectMany(e => e.Events).Select(e => CanonicalJson.Text<DomainEvent>(e)),
            replay.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)));
        var before = resumed.Capture();
        var duplicate = CommandLogService.Replay(FilePath("commands.jsonl"), resumed);
        Assert.Equal(resumed.NextOperationSequence, duplicate.AlreadyProcessed);
        Assert.Empty(duplicate.Events);
        Assert.Same(before, resumed.Capture());
    }

    [Fact]
    public void OperationWatermarkSurvivesSave()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var request = session.Request(new CommandOperation(new DevelopCommand(Id(1), 1, 0,
            new(1, 1, [2], DomesticActivity.Commerce))), id: Id(1));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        session = SaveService.Decode(SaveService.Encode(session), definition);
        var before = session.Capture();
        var duplicate = session.Apply(request);
        Assert.Equal(SessionDisposition.AlreadyProcessed, duplicate.Disposition);
        Assert.Empty(duplicate.Events);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request with { ControllerId = "ai:2" }).Disposition);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request with { Operation = new ChronicleOperation("changed") }).Disposition);
        Assert.Equal(SessionRejection.OutOfOrder, session.Apply(new(Id(2), 10, "player", new ChronicleOperation("later"))).Rejection);
        var invalid = new SessionRequest(Id(2), 1, "player", new CommandOperation(new DevelopCommand(Id(2), 1, 1,
            new(1, 1, [2], DomesticActivity.Commerce))));
        Assert.Equal(SessionDisposition.Rejected, session.Apply(invalid).Disposition);
        Assert.Same(before, session.Capture());
        Assert.Equal(SessionDisposition.Accepted, session.Apply(invalid with { Operation = new ChronicleOperation("same rejected ID is reusable") }).Disposition);
        Assert.Equal(1, session.Snapshot.Simulation.NextSequence);
    }

    [Fact]
    public void LogStopsAtFirstRefusalAndRetainsOnlyAcceptedPrefix()
    {
        var session = GameSession.Start(Fixture(), 190);
        var first = new SessionRequest(Id(1), 0, "player", new ChronicleOperation("first"));
        var bad = new SessionRequest(Id(2), 1, "ai:2", new ChronicleOperation("bad"));
        var third = new SessionRequest(Id(3), 2, "player", new ChronicleOperation("unreached"));
        CommandLogService.Write(FilePath("log.jsonl"), new(SessionProtocol.LogFormatVersion, SessionProtocol.RulesVersion, 190), [first, bad, third]);
        var result = CommandLogService.Replay(FilePath("log.jsonl"), session);
        Assert.Equal(3, result.FirstFailure!.Line);
        Assert.Equal(1, result.Accepted);
        Assert.Equal("first", Assert.Single(session.Chronicle).Text);
        Assert.Equal(1, session.NextOperationSequence);
    }

    [Theory]
    [InlineData("formatVersion")]
    public void UnknownVersionsAreExplicitlyRejected(string field)
    {
        var session = GameSession.Start(Fixture(), 190);
        var root = JsonNode.Parse(SaveService.Encode(session))!;
        root[field] = JsonValue.Create(999);
        Assert.Equal(SaveFailure.UnsupportedVersion, Assert.Throws<SaveException>(() =>
            SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), Fixture())).Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuleVersionMismatchWarnsWithoutBlockingRestorationOrChangingSavedState(bool playerLoad)
    {
        var definition = Fixture();
        var original = GameSession.Start(definition, 190);
        original.AddChronicleNote("saved progress");
        var envelope = SaveService.Capture(original) with { RulesVersion = "older-rules" };
        var restored = playerLoad ? SaveService.Load(envelope, definition) : SaveService.Decode(CanonicalJson.Encode(envelope), definition);
        var warning = Assert.Single(restored.RulesVersionWarnings);
        Assert.Equal(envelope.RulesVersion, warning.SavedVersion);
        Assert.Equal(SessionProtocol.RulesVersion, warning.CurrentVersion);
        Assert.Equal(original.Chronicle, restored.Chronicle);
        Assert.False(restored.HasUnsavedChanges);
        if (!playerLoad) StateAssert.Equal(original.Capture(), restored.Capture());
        var saved = SaveService.Capture(restored);
        Assert.Equal(SessionProtocol.RulesVersion, saved.RulesVersion);
        Assert.Empty(SaveService.Decode(CanonicalJson.Encode(saved), definition).RulesVersionWarnings);
        Assert.DoesNotContain("rulesVersionWarnings", Encoding.UTF8.GetString(SaveService.Encode(restored)));
        Assert.Empty(original.RulesVersionWarnings);
    }

    [Fact]
    public void RuleVersionMismatchWarnsAndCommandLogStillAppliesCommands()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        string path = FilePath("older-rules.jsonl");
        var request = session.Request(new ChronicleOperation("old log command"));
        var header = new CommandLogHeader(SessionProtocol.LogFormatVersion, "older-rules", 190);
        CommandLogService.Write(path, header, [request]);
        var result = CommandLogService.Replay(path, session);
        Assert.Null(result.FirstFailure);
        Assert.Equal(1, result.Accepted);
        Assert.Equal("old log command", Assert.Single(session.Chronicle).Text);
        Assert.Equal("older-rules", Assert.Single(session.RulesVersionWarnings).SavedVersion);
    }

    [Fact]
    public void RuleVersionMismatchWarnsAndRecordingStillValidatesRealReplayDifferences()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        session.AddChronicleNote("old recording command");
        var envelope = StrictJson.Deserialize<RecordingEnvelope>(RecordingService.Encode(session.EndRecording()))
            with { RulesVersion = "older-rules" };
        var warnings = new List<RulesVersionWarning>();
        var replay = RecordingService.Replay(CanonicalJson.Encode(envelope), definition, GameSession.EngineFactory(definition), warnings.Add);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.Equal("older-rules", Assert.Single(replay.Session.RulesVersionWarnings).SavedVersion);
        Assert.Single(warnings);
        envelope = envelope with { Recording = envelope.Recording with { Operations = envelope.Recording.Operations.SetItem(0,
            envelope.Recording.Operations[0] with { Events = [new XunAdvancedEvent(session.Snapshot.Clock, session.Snapshot.Clock)] }) } };
        warnings.Clear();
        Assert.Contains("diverged", Assert.Throws<RecordingException>(() => RecordingService.Replay(
            CanonicalJson.Encode(envelope), definition, GameSession.EngineFactory(definition), warnings.Add, verifyEvents: true)).Message);
        Assert.Single(warnings);
    }

    [Fact]
    public void InvalidSaveDataIsRejectedWhileChangedRuleDefinitionsCanLoad()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        byte[] bytes = SaveService.Encode(session);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(bytes[..^8], definition)).Failure);
        var root = JsonNode.Parse(bytes)!;
        root["session"]!["world"]!["simulation"]!.AsObject().Remove("randomStreams");
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
        root = JsonNode.Parse(bytes)!;
        root["session"]!["world"]!["corps"]![0]!["treasury"]!["money"] = 1;
        Assert.Equal(1u, SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition).Snapshot.Corps[0].Treasury.Money);
        var changed = definition with { Information = definition.Information with { Offices = [definition.Information.Offices[0] with { Salary = 21 }] } };
        StateAssert.Equal(session.Capture(), SaveService.Decode(bytes, changed).Capture());
        var envelope = StrictJson.Deserialize<SaveEnvelope>(bytes);
        var world = envelope.Session.World with { Officers = envelope.Session.World.Officers.Select(o => o.Id == 2 ? o with { OwnerId = 999 } : o).ToArray() };
        var corrupted = envelope with { Session = envelope.Session with { World = world } };
        var edited = CanonicalJson.Encode(corrupted);
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(edited, definition)).Failure);
        Assert.Equal(997u, session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
    }

    [Fact]
    public void PendingTaskWithoutExecutorOrCompiledRuleCannotRestore()
    {
        var definition = Fixture();
        var rules = new FixtureRules();
        var session = GameSession.Start(definition, 190, w => new(w, rules));
        session.Apply(session.Request(new CommandOperation(new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1)), id: Id(1)));
        byte[] bytes = SaveService.Encode(session);
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(bytes, definition)).Failure);
        var envelope = StrictJson.Deserialize<SaveEnvelope>(bytes);
        var corrupted = envelope with { Session = envelope.Session with { World = envelope.Session.World with
            { Officers = envelope.Session.World.Officers.Where(o => o.Id != 2).ToArray(), Treasures = [] } } };
        byte[] edited = CanonicalJson.Encode(corrupted);
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(edited, definition, w => new(w, rules))).Failure);
    }

    [Fact]
    public void AtomicWriteFailurePreservesPriorSaveAndCurrentSession()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        string path = FilePath("save.json");
        SaveService.Write(path, session);
        byte[] old = File.ReadAllBytes(path);
        session.AddChronicleNote("new state");
        var before = session.Capture();
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(SaveFailure.IoFailure, Assert.Throws<SaveException>(() =>
            SaveService.Write(path, session, () => throw new IOException("injected replace failure"))).Failure);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(old, File.ReadAllBytes(path));
        Assert.Same(before, session.Capture());
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Empty(SaveService.Read(path, definition).Chronicle);
    }

    [Fact]
    public void FailedCompositeAdvancePreservesControlsWorldAndWatermark()
    {
        var rules = new FixtureRules();
        var session = GameSession.Start(Fixture(), 190, w => new(w, rules));
        session.Apply(session.Request(new CommandOperation(new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1)), id: Id(1)));
        session.AdvanceXun();
        rules.Fail = true;
        var before = session.Capture();
        Assert.Throws<InvalidOperationException>(() => session.AdvanceXun());
        Assert.Same(before, session.Capture());
        Assert.Equal(0, session.Snapshot.Simulation.ActiveControl);
        rules.Fail = false;
        session.AdvanceXun();
        Assert.Empty(session.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void StateComparisonIgnoresCollectionAndPropertyOrderButPreservesMeaning()
    {
        var definition = Fixture();
        var reordered = definition with { Forces = definition.Forces.Reverse().ToArray(), Settlements = definition.Settlements.Reverse().ToArray(),
            Officers = definition.Officers.Reverse().ToArray(), Information = definition.Information with { Corps = definition.Information.Corps.Reverse().ToArray() } };

        var a = GameSession.Start(definition, 190);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            var b = GameSession.Start(reordered, 190);
            StateAssert.Equal(a.Capture(), b.Capture());
            var root = JsonNode.Parse(SaveService.Encode(a))!.AsObject();
            var reversed = new JsonObject(root.Reverse().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())));
            StateAssert.Equal(a.Capture(), SaveService.Decode(Encoding.UTF8.GetBytes(reversed.ToJsonString()), reordered).Capture());
        }
        finally { CultureInfo.CurrentCulture = culture; }
        a.EndControl(new("player"));
        StateAssert.NotEqual(a.Snapshot, GameSession.Start(definition, 190).Snapshot);
    }

    [Fact]
    public void DiagnosticComparisonIncludesSessionChangesWithoutWorldMutation()
    {
        var before = GameSession.Start(Fixture(), 190).Capture();
        var changed = before with
        {
            NextOperationSequence = before.NextOperationSequence + 1
        };
        Assert.Same(before.World, changed.World);
        Assert.True(SessionDiagnostics.AreEqual(before, before with { World = before.World with { Revision = before.World.Revision + 1 } }));
        Assert.False(SessionDiagnostics.AreEqual(before, changed));
        Assert.Equal("$.nextOperationSequence", SessionDiagnostics.Compare(before, changed)?.Path);
        var worldChanged = before with { World = before.World with { Clock = new(190, 1, 2) } };
        Assert.False(SessionDiagnostics.AreEqual(before, worldChanged));
        Assert.Equal("$.world.clock.day", SessionDiagnostics.Compare(before, worldChanged)?.Path);
        Assert.False(SessionDiagnostics.AreEqual(before, before with { NextOperationSequence = before.NextOperationSequence + 1 }));
    }

    [Fact]
    public void SavingDuringAnOperationIsRejectedBeforeCreatingAFile()
    {
        var rules = new FixtureRules();
        var session = GameSession.Start(Fixture(), 190, w => new(w, rules));
        int probes = 0;
        rules.DuringDailyUpdate = () =>
        {
            probes++;
            Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Write(FilePath("partial.json"), session)).Failure);
        };
        session.AdvanceXun();
        Assert.Equal(10, probes);
        Assert.False(File.Exists(FilePath("partial.json")));
        SaveService.Write(FilePath("stable.json"), session);
        Assert.True(File.Exists(FilePath("stable.json")));
    }

    [Fact]
    public void PersonnelRewardPersistsReplaysAndKeepsOtherForceLoyalty()
    {
        var initial = Fixture();
        var definition = initial with
        {
            Officers = initial.Officers.Select(o => o.State.Id == 2 ? o with
            {
                State = o.State with { Righteousness = 15, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 80).Add(2, 255) }
            } : o).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        var request = session.Request(new CommandOperation(new RewardOfficerCommand(Id(80), 1, 0, 2)), id: Id(80));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var person = restored.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(82, person.Loyalty);
        Assert.Equal(255, person.LoyaltyTo(2));
        Assert.True(person.RewardedThisXun);
        var advance = session.Request(new AdvanceOperation());
        Equal(session.Apply(advance), restored.Apply(advance));
        var second = session.Request(new CommandOperation(new RewardOfficerCommand(Id(81), 1, session.Snapshot.Simulation.NextSequence, 2)), id: Id(81));
        Equal(session.Apply(second), restored.Apply(second));
        Assert.Equal(84, restored.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        var replayed = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(SaveService.Encode(restored), SaveService.Encode(replayed.Session));
    }

    [Fact]
    public void DuplicatePropertiesUnknownFieldsAndNullEntriesAreRejected()
    {
        var definition = Fixture();
        byte[] bytes = SaveService.Encode(GameSession.Start(definition, 190));
        string versionProperty = $"\"formatVersion\":{SessionProtocol.SaveFormatVersion}";
        string duplicate = Encoding.UTF8.GetString(bytes).Replace(versionProperty, $"{versionProperty},{versionProperty}", StringComparison.Ordinal);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(duplicate), definition)).Failure);
        var root = JsonNode.Parse(bytes)!;
        root["unexpected"] = true;
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
        root = JsonNode.Parse(bytes)!;
        root["session"]!["world"]!["officers"]![0] = null;
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
    }

    private sealed class TerminalRules : SimulationRules
    {
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage == ResolutionStage.TerminalCheck)
                world.SetSimulation(world.Simulation with { TerminalReason = "fixture-terminal" });
        }
    }

    [Fact]
    public void TerminalSessionRoundTripsWithoutRestartingSimulation()
    {
        var definition = Fixture();
        var terminal = GameSession.Start(definition, 190, w => new(w, new TerminalRules()));
        SaveService.Write(FilePath("terminal.json"), terminal);
        var restored = SaveService.Read(FilePath("terminal.json"), definition);
        StateAssert.Equal(terminal.Capture(), restored.Capture());
        Assert.Equal("fixture-terminal", restored.Snapshot.Simulation.TerminalReason);
        Assert.Empty(restored.InitialEvents);
        var before = restored.Capture();
        Assert.Empty(restored.AdvanceXun().Events);
        Assert.Equal(SessionRejection.Terminal, restored.Apply(restored.Request(new ChronicleOperation("too late"))).Rejection);
        Assert.Same(before, restored.Capture());
    }

    [Fact]
    public void InternallyValidUnknownIdentityIsRejectedBeforeProjection()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var snapshot = session.Snapshot;
        var unknown = new SimulationEngine(WorldState.Create(snapshot.Clock, 190, snapshot.Forces, snapshot.Settlements,
            snapshot.Officers.Append(new OfficerState(999, 1, 1, new(1, 1, 1, 1), false)), snapshot.Units,
            snapshot.Corps, snapshot.Treasures, snapshot.Relations, snapshot.Simulation.Controls)).Snapshot;
        var envelope = StrictJson.Deserialize<SaveEnvelope>(SaveService.Encode(session));
        var corrupted = envelope with { Session = envelope.Session with { World = unknown } };
        var bytes = CanonicalJson.Encode(corrupted);
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(bytes, definition)).Failure);
    }

    [Fact]
    public void UnknownEnumInRecentRecordIsRejected()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.Develop(new(1, 1, [2], DomesticActivity.Commerce));
        var root = JsonNode.Parse(SaveService.Encode(session))!;
        root["session"]!["records"]![0]!["activity"] = 999;
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
    }

    [Fact]
    public void DefaultPlayerSaveExcludesOperationHistoryAndCanContinueAfterRestore()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        for (int xun = 0; xun < 3; xun++)
        {
            var request = session.Request(new EndControlOperation());
            var result = session.Apply(request with { Operation = new CommandOperation(new WaitCommand(request.Id, 1, session.Snapshot.Simulation.NextSequence)) });
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            session.AdvanceXun();
            DecisionResponses.DeclineAll(session);
        }
        Assert.False(session.IsRecording);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        byte[] final = SaveService.Encode(session);
        string json = Encoding.UTF8.GetString(final);
        Assert.DoesNotContain("acceptedCommandIds", json);
        Assert.DoesNotContain("journal", json);
        Assert.DoesNotContain("events", json);
        var restored = SaveService.Decode(final, definition);
        var next = session.Request(new AdvanceOperation());
        Equal(session.Apply(next), restored.Apply(next));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
    }

    [Fact]
    public void AutomaticRecordsAreBoundedWhilePlayerNotesArePreserved()
    {
        var value = Fixture();
        var definition = value with { Information = value.Information with { Corps = value.Information.Corps.Select(f => f with { Treasury = f.Treasury with { Money = 100000 } }).ToArray() },
            Settlements = value.Settlements.Select(s => s with { MaxCommerce = 2500 }).ToArray(),
            Officers = value.Officers.Select(o => o with { State = o.State.WithLoyalty(o.State.OwnerId!.Value, 100) }).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.AddChronicleNote("player note");
        List<GameClock> completedDates = [];
        for (int i = 0; i < GameSession.RecentRecordLimit + 10; i++)
        {
            Assert.True(session.Develop(new(1, 1, [2], DomesticActivity.Commerce)).Accepted);
            completedDates.Add(session.Snapshot.Clock);
            completedDates.AddRange(session.AdvanceXun().Events.OfType<DevelopmentCompletedEvent>().Select(e => e.Clock));
            DecisionResponses.DeclineAll(session);
        }
        Assert.Equal(GameSession.RecentRecordLimit, session.Records.Count);
        Assert.Equal(completedDates.TakeLast(GameSession.RecentRecordLimit), session.Records.Select(r => r.Clock));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Records, restored.Records);
        Assert.Equal("player note", Assert.Single(restored.Chronicle).Text);
    }

    [Fact]
    public void MidGameRecordingIsIndependentFromPlayerSaveAndReplaysFromDisk()
    {
        var definition = Fixture();
        var rules = new FixtureRules();
        Func<WorldState, SimulationEngine> factory = w => new(w, rules);
        var session = GameSession.Start(definition, 190, factory);
        session.AddChronicleNote("before recording");
        session.Apply(session.Request(new CommandOperation(new StartTaskCommand(Id(1), 1, 0, "fixture-investment", 2, 1)), id: Id(1)));
        session.AdvanceXun();
        var plain = SaveService.Decode(SaveService.Encode(session), definition, factory);
        session.BeginRecording();
        Assert.Throws<InvalidOperationException>(() => session.BeginRecording());
        var origin = session.Capture();
        var advance = session.Request(new AdvanceOperation());
        Equal(session.Apply(advance), plain.Apply(advance));
        Assert.Equal(SaveService.Encode(plain), SaveService.Encode(session));
        var completed = session.EndRecording();
        StateAssert.Equal(origin, completed.Initial);
        Assert.Single(completed.Operations);
        Assert.False(session.IsRecording);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        string path = FilePath("midgame.recording.json");
        RecordingService.Write(path, completed);
        var replayed = RecordingService.Replay(File.ReadAllBytes(path), definition, factory);
        Assert.Equal(SaveService.Encode(plain), SaveService.Encode(replayed.Session));
        Assert.Equal(1, replayed.Accepted);
        Assert.False(replayed.Session.IsRecording);
        rules.Fail = true;
        Assert.Equal(advance.Sequence, Assert.Throws<RecordingException>(() =>
            RecordingService.Replay(File.ReadAllBytes(path), definition, factory)).Sequence);
        rules.Fail = false;
        Assert.Empty(replayed.Session.InitialEvents);
        session.BeginRecording();
        Assert.Empty(session.CaptureRecording().Operations);
        StateAssert.Equal(session.Capture(), session.CaptureRecording().Initial);
        Assert.False(SaveService.Decode(SaveService.Encode(session), definition, factory).IsRecording);
    }

    [Fact]
    public void RecordingRejectsInvalidFormatAndEventVerificationReportsFirstDivergence()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.AdvanceXun();
        session.BeginRecording();
        session.AddChronicleNote("recorded");
        session.AdvanceXun();
        byte[] bytes = RecordingService.Encode(session.CaptureRecording());
        Assert.Throws<RecordingException>(() => RecordingService.Replay(bytes[..^5], definition, GameSession.EngineFactory(definition)));
        var root = JsonNode.Parse(bytes)!;
        root["formatVersion"] = 999;
        Assert.Throws<RecordingException>(() => RecordingService.Replay(Encoding.UTF8.GetBytes(root.ToJsonString()), definition, GameSession.EngineFactory(definition)));
        var changed = definition with { Information = definition.Information with { Corps = definition.Information.Corps.Select(f => f with { Treasury = f.Treasury with { Money = 100 } }).ToArray() } };
        StateAssert.Equal(session.Capture(), RecordingService.Replay(bytes, changed, GameSession.EngineFactory(changed)).Session.Capture());
        var envelope = StrictJson.Deserialize<RecordingEnvelope>(bytes);
        var operations = envelope.Recording.Operations.SetItem(1, envelope.Recording.Operations[1] with { Events = [] });
        var divergent = envelope with { Recording = envelope.Recording with { Operations = operations } };
        var encoded = CanonicalJson.Encode(divergent);
        Assert.Equal(2, Assert.Throws<RecordingException>(() => RecordingService.Replay(encoded, definition, GameSession.EngineFactory(definition), verifyEvents: true)).Sequence);
        Assert.Null(SessionDiagnostics.Compare(session.Capture(),
            RecordingService.Replay(encoded, definition, GameSession.EngineFactory(definition)).Session.Capture()));
        var outOfOrder = envelope with { Recording = envelope.Recording with { Operations = envelope.Recording.Operations.Reverse().ToImmutableArray() } };
        encoded = CanonicalJson.Encode(outOfOrder);
        Assert.Equal(2, Assert.Throws<RecordingException>(() => RecordingService.Replay(encoded, definition, GameSession.EngineFactory(definition))).Sequence);
    }

    private static void Equal(SessionResult a, SessionResult b)
    {
        Assert.Equal(SessionDisposition.Accepted, a.Disposition);
        Assert.Equal(a.Disposition, b.Disposition);
        StateAssert.Equal(a.Snapshot, b.Snapshot);
        EventAssert.Equal(a.Events, b.Events);
    }
}
