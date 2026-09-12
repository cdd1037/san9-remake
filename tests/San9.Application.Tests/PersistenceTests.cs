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
        Assert.Equal(original.Snapshot.Forces, loaded.Snapshot.Forces);
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
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private static ScenarioInitialState Fixture()
    {
        var value = G01ScenarioTests.Fixture();
        return value with
        {
            Units = [new(1, 1, San9.Core.Domain.MapCoordinates.Center(new(3, 4)), 100)],
            Information = new([], [new(1, "office", 1000, 20)], [new(1, "treasure", 0, 10, 5, null)],
                [new(1, 1, 1, null), new(2, 2, 4, null)], [new(1, 2, TreasureDiscovery.Discovered, null)])
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
            internal override bool Validate(WorldMutation world, PersistentTask task) => world.Forces[task.ForceId].Money >= 50;
            internal override void Complete(WorldMutation world, PersistentTask task, List<DomainEvent> events)
            {
                world.Replace(world.Forces[task.ForceId] with { Money = world.Forces[task.ForceId].Money - 50 });
                int increment = 1 + (int)(world.NextRandom("fixture-investment") % 10);
                world.Replace(world.Settlements[task.OriginSettlementId] with { Commerce = owner.Fail ? 9999 : world.Settlements[task.OriginSettlementId].Commerce + increment });
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
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
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
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
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
            Assert.Equal(continuous.Capture().StateHash, resumed.Capture().StateHash);
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
        Assert.Equal(continuous.Capture().StateHash, resumed.Capture().StateHash);
        Assert.Equal(continuous.Records, resumed.Records);
        CommandLogService.Write(FilePath("commands.jsonl"), continuous);
        var replayed = GameSession.Start(definition, 190, factory);
        var replay = CommandLogService.Replay(FilePath("commands.jsonl"), replayed);
        Assert.Null(replay.FirstFailure);
        Assert.Equal(continuous.Capture().StateHash, replayed.Capture().StateHash);
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
        CommandLogService.Write(FilePath("log.jsonl"), new(SessionProtocol.LogFormatVersion, SessionProtocol.RulesVersion, CanonicalJson.Version, session.ContentFingerprint, 190), [first, bad, third]);
        var result = CommandLogService.Replay(FilePath("log.jsonl"), session);
        Assert.Equal(3, result.FirstFailure!.Line);
        Assert.Equal(1, result.Accepted);
        Assert.Equal("first", Assert.Single(session.Chronicle).Text);
        Assert.Equal(1, session.NextOperationSequence);
    }

    [Theory]
    [InlineData("formatVersion")]
    [InlineData("rulesVersion")]
    [InlineData("hashVersion")]
    public void UnknownVersionsAreExplicitlyRejected(string field)
    {
        var session = GameSession.Start(Fixture(), 190);
        var root = JsonNode.Parse(SaveService.Encode(session))!;
        root[field] = field == "rulesVersion" ? JsonValue.Create("unknown") : JsonValue.Create(999);
        Assert.Equal(SaveFailure.UnsupportedVersion, Assert.Throws<SaveException>(() =>
            SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), Fixture())).Failure);
    }

    [Fact]
    public void CorruptionMissingFieldsAndContentMismatchAreExplicit()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        byte[] bytes = SaveService.Encode(session);
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(bytes[..^8], definition)).Failure);
        var root = JsonNode.Parse(bytes)!;
        root["session"]!["world"]!["simulation"]!.AsObject().Remove("randomStreams");
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
        root = JsonNode.Parse(bytes)!;
        root["session"]!["world"]!["forces"]![0]!["money"] = 1;
        Assert.Equal(SaveFailure.IntegrityMismatch, Assert.Throws<SaveException>(() => SaveService.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), definition)).Failure);
        var changed = definition with { Information = definition.Information with { Offices = [definition.Information.Offices[0] with { Salary = 21 }] } };
        Assert.Equal(SaveFailure.ContentMismatch, Assert.Throws<SaveException>(() => SaveService.Decode(bytes, changed)).Failure);
        var envelope = StrictJson.Deserialize<SaveEnvelope>(bytes);
        var world = envelope.Session.World with { Officers = envelope.Session.World.Officers.Select(o => o.Id == 2 ? o with { OwnerId = 999 } : o).ToArray() };
        var corrupted = envelope with { Session = envelope.Session with { World = world } };
        var resigned = CanonicalJson.Encode(corrupted with { IntegrityHash = SaveService.Integrity(corrupted) });
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(resigned, definition)).Failure);
        Assert.Equal(997u, session.Snapshot.Forces[0].Money);
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
        byte[] resigned = CanonicalJson.Encode(corrupted with { IntegrityHash = SaveService.Integrity(corrupted) });
        Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() => SaveService.Decode(resigned, definition, w => new(w, rules))).Failure);
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
        Assert.Equal(SaveFailure.IoFailure, Assert.Throws<SaveException>(() =>
            SaveService.Write(path, session, () => throw new IOException("injected replace failure"))).Failure);
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
        Assert.Throws<ArgumentException>(() => session.AdvanceXun());
        Assert.Same(before, session.Capture());
        Assert.Equal(0, session.Snapshot.Simulation.ActiveControl);
        rules.Fail = false;
        session.AdvanceXun();
        Assert.Empty(session.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void CanonicalHashesIgnoreCollectionAndPropertyOrderButPreserveMeaning()
    {
        var definition = Fixture();
        var reordered = definition with { Forces = definition.Forces.Reverse().ToArray(), Settlements = definition.Settlements.Reverse().ToArray(),
            Officers = definition.Officers.Reverse().ToArray(), Information = definition.Information with { Corps = definition.Information.Corps.Reverse().ToArray() } };
        Assert.Equal(GameSession.Fingerprint(definition), GameSession.Fingerprint(reordered));
        var translated = definition with { Forces = definition.Forces.Select(f => f with { NameKey = "translated", ColorCode = 10 }).ToArray() };
        Assert.Equal(GameSession.Fingerprint(definition), GameSession.Fingerprint(translated));
        var a = GameSession.Start(definition, 190);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            var b = GameSession.Start(reordered, 190);
            Assert.Equal(a.Capture().StateHash, b.Capture().StateHash);
            var root = JsonNode.Parse(SaveService.Encode(a))!.AsObject();
            var reversed = new JsonObject(root.Reverse().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())));
            Assert.Equal(a.Capture().StateHash, SaveService.Decode(Encoding.UTF8.GetBytes(reversed.ToJsonString()), reordered).Capture().StateHash);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        a.EndControl(new("player"));
        Assert.NotEqual(a.Snapshot.StateHash, GameSession.Start(definition, 190).Snapshot.StateHash);
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
        Assert.Equal(terminal.Capture().StateHash, restored.Capture().StateHash);
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
        var bytes = CanonicalJson.Encode(corrupted with { IntegrityHash = SaveService.Integrity(corrupted) });
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
    public void FortyYearsOfDefaultPlayHasNoHistoryGrowthAndCanContinueAfterRestore()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        byte[] initial = SaveService.Encode(session);
        for (int xun = 0; xun < 40 * 36; xun++)
        {
            var request = session.Request(new EndControlOperation());
            var result = session.Apply(request with { Operation = new CommandOperation(new WaitCommand(request.Id, 1, session.Snapshot.Simulation.NextSequence)) });
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            session.AdvanceXun();
        }
        Assert.Equal(230, session.Snapshot.Clock.Year);
        Assert.False(session.IsRecording);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        byte[] final = SaveService.Encode(session);
        Assert.InRange(final.Length - initial.Length, 0, 100);
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
        var definition = value with { Forces = value.Forces.Select(f => f with { Money = 100000 }).ToArray(),
            Settlements = value.Settlements.Select(s => s with { MaxCommerce = 2500 }).ToArray() };
        var session = GameSession.Start(definition, 190);
        session.AddChronicleNote("player note");
        for (int i = 0; i < GameSession.RecentRecordLimit + 10; i++)
        {
            Assert.True(session.Develop(new(1, 1, [2], DomesticActivity.Commerce)).Accepted);
            session.AdvanceXun();
        }
        Assert.Equal(GameSession.RecentRecordLimit, session.Records.Count);
        Assert.Equal(new GameClock(190, 4, 11), session.Records[0].Clock);
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
        Assert.Equal(origin.StateHash, completed.Initial.StateHash);
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
        Assert.Equal(session.Capture().StateHash, session.CaptureRecording().Initial.StateHash);
        Assert.False(SaveService.Decode(SaveService.Encode(session), definition, factory).IsRecording);
    }

    [Fact]
    public void RecordingRejectsCorruptionVersionContentAndReportsFirstDivergentSequence()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        session.AdvanceXun();
        session.BeginRecording();
        session.AddChronicleNote("recorded");
        session.AdvanceXun();
        byte[] bytes = RecordingService.Encode(session.CaptureRecording());
        Assert.Throws<RecordingException>(() => RecordingService.Replay(bytes[..^5], definition, w => new(w)));
        var root = JsonNode.Parse(bytes)!;
        root["formatVersion"] = 999;
        Assert.Throws<RecordingException>(() => RecordingService.Replay(Encoding.UTF8.GetBytes(root.ToJsonString()), definition, w => new(w)));
        root = JsonNode.Parse(bytes)!;
        root["recording"]!["operations"]![0]!["worldHash"] = "tampered";
        Assert.Throws<RecordingException>(() => RecordingService.Replay(Encoding.UTF8.GetBytes(root.ToJsonString()), definition, w => new(w)));
        var changed = definition with { Forces = definition.Forces.Select(f => f with { Money = 100 }).ToArray() };
        Assert.Throws<RecordingException>(() => RecordingService.Replay(bytes, changed, w => new(w)));
        var envelope = StrictJson.Deserialize<RecordingEnvelope>(bytes);
        var operations = envelope.Recording.Operations.SetItem(0, envelope.Recording.Operations[0] with { WorldHash = new string('0', 64) });
        var divergent = envelope with { Recording = envelope.Recording with { Operations = operations } };
        var resigned = CanonicalJson.Encode(divergent with { IntegrityHash = RecordingService.Integrity(divergent) });
        Assert.Equal(1, Assert.Throws<RecordingException>(() => RecordingService.Replay(resigned, definition, w => new(w))).Sequence);
        var outOfOrder = envelope with { Recording = envelope.Recording with { Operations = envelope.Recording.Operations.Reverse().ToImmutableArray() } };
        resigned = CanonicalJson.Encode(outOfOrder with { IntegrityHash = RecordingService.Integrity(outOfOrder) });
        Assert.Equal(2, Assert.Throws<RecordingException>(() => RecordingService.Replay(resigned, definition, w => new(w))).Sequence);
    }

    private static void Equal(SessionResult a, SessionResult b)
    {
        Assert.Equal(SessionDisposition.Accepted, a.Disposition);
        Assert.Equal(a.Disposition, b.Disposition);
        Assert.Equal(a.Snapshot.StateHash, b.Snapshot.StateHash);
        Assert.Equal(a.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)), b.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)));
    }
}
