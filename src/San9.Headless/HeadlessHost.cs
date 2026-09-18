using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Headless;

public sealed class HeadlessHost
{
    private readonly HeadlessReport _report = new();
    private GameSession? _session;
    private string? _reportPath;
    private GameClock _started;

    public static int Run(string[] args) => new HeadlessHost().Execute(args);

    private int Execute(string[] args)
    {
        var timer = Stopwatch.StartNew();
        int exitCode = 0;
        try
        {
            var options = Parse(args);
            _report.Mode = args[0];
            if (_report.Mode == "verify")
            {
                string output = Path.GetFullPath(Required(options, "output"));
                _reportPath = Path.Combine(output, "report.json");
                Verify(options, output);
            }
            else
            {
                _reportPath = Path.GetFullPath(Required(options, "report"));
                if (_report.Mode == "replay") ReplayRecording(options);
                else Replay(options);
            }
            _report.Success = true;
        }
        catch (UsageException error) { exitCode = Fail(2, "usage", error); }
        catch (ContentException error) { exitCode = Fail(3, "content", error); }
        catch (SaveException error) { exitCode = Fail(4, "save-" + error.Failure, error); }
        catch (RecordingException error) { exitCode = Fail(5, "recording", error); _report.FailureSequence = error.Sequence; }
        catch (CommandLogException error) { exitCode = Fail(5, "replay", error, error.Line); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { exitCode = Fail(6, "io", error); }
        catch (VerificationException error) { exitCode = Fail(7, "verification", error); }
        catch (Exception error) { exitCode = Fail(8, "unexpected", error); }
        finally
        {
            if (_session is not null)
            {
                _report.ContentFingerprint = _session.ContentFingerprint;
                _report.Seed = _session.Snapshot.Simulation.InitialSeed;
                _report.FinalHash = _session.Capture().StateHash;
                _report.WorldHash = _session.Snapshot.StateHash;
                _report.CompletedXun = (_session.Snapshot.Clock.DayNumber - _started.DayNumber) / 10;
                _report.Coverage = new(_session.Snapshot.Forces.Count, _session.Snapshot.Settlements.Count,
                    _session.Snapshot.Officers.Count, _session.Snapshot.Units.Count, _session.Snapshot.Corps.Count,
                    _session.Snapshot.Treasures.Count, _session.Snapshot.Relations.Count);
            }
            _report.ElapsedMilliseconds = timer.ElapsedMilliseconds;
            if (_reportPath is not null)
            {
                try { Write(_reportPath, CanonicalJson.Encode(_report)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { exitCode = Fail(6, "report-io", error); }
            }
            Console.WriteLine(CanonicalJson.Text(_report));
        }
        return exitCode;
    }

    private void SummarizeEvents(IEnumerable<DomainEvent> events)
    {
        foreach (var entry in events)
        {
            string? diplomacyStrategy = entry switch
            {
                GiftResolvedEvent => "gift-resolved",
                PrisonerReturnResolvedEvent => "prisoner-return-resolved",
                CompensationQuotedEvent => "compensation-quoted",
                AttackRequestResolvedEvent => "attack-request-resolved",
                AttackRequestEstablishedEvent => "attack-request-established",
                AttackRequestEndedEvent => "attack-request-ended",
                SurrenderResolvedEvent => "surrender-resolved",
                ForceSurrenderedEvent => "force-surrendered",
                AllianceFormedEvent => "alliance-formed",
                AllianceEndedEvent => "alliance-ended",
                AllianceParticipationDecidedEvent => "alliance-participation",
                UnitStrategyResolvedEvent strategy => "unit-strategy-" + strategy.Strategy.ToString().ToLowerInvariant(),
                BurnResolvedEvent => "burn-resolved",
                AlienateResolvedEvent => "alienate-resolved",
                SeizeResolvedEvent => "seize-resolved",
                GossipResolvedEvent => "gossip-resolved",
                _ => null
            };
            if (diplomacyStrategy is not null)
                _report.DiplomacyStrategyEvents[diplomacyStrategy] = _report.DiplomacyStrategyEvents.GetValueOrDefault(diplomacyStrategy) + 1;
            if (entry is DevelopmentCompletedEvent developed)
            {
                _report.DevelopmentCommands++;
                string activity = developed.Command.Activity.ToString();
                _report.Activities[activity] = _report.Activities.GetValueOrDefault(activity) + 1;
            }
            if (entry is ResourceChangedEvent resource)
            {
                _report.ResourceShortfall += resource.Shortfall;
                _report.SaturationLoss += resource.SaturationLoss;
            }
            string? military = entry switch
            {
                UnitDeployedEvent => "unit-deployed",
                FacilityConstructionChangedEvent { Phase: "started" } => "construction-started",
                FacilityConstructionChangedEvent { Phase: "completed" } => "construction-completed",
                FacilityConstructionChangedEvent => "construction-changed",
                TacticActivatedEvent => "tactic-activated",
                UnitMovedEvent { Status: NavigationStatus.Unreachable } => "unit-blocked",
                UnitMovedEvent { Status: NavigationStatus.Waiting } => "unit-waiting",
                UnitMovedEvent => "unit-moved",
                UnitOrderChangedEvent { Reason: "target-invalid" } => "unit-target-invalid",
                UnitOrderChangedEvent => "unit-order-changed",
                  UnitAbsorbedEvent => "unit-absorbed",
                  BattleDamageEvent => "battle-damage",
                  UnitTerminatedEvent => "unit-terminated",
                  SettlementCapturedEvent => "settlement-captured",
                  FacilityDestroyedEvent => "facility-destroyed",
                  ForceEliminatedEvent => "force-eliminated",
                _ => null
            };
            if (military is not null)
                _report.MilitaryEvents[military] = _report.MilitaryEvents.GetValueOrDefault(military) + 1;
            string? personnel = entry switch
            {
                TaskStartedEvent { Task.Journey: not null } => "journey-started",
                SearchResolvedEvent { DiscoveredOfficerId: not null } => "person-discovered",
                SearchResolvedEvent => "search-no-person",
                RecruitmentResolvedEvent { Success: true } => "recruitment-success",
                RecruitmentResolvedEvent => "recruitment-failure",
                OfficerMovedEvent => "officer-moved",
                AdviserAppointedEvent => "adviser-appointed",
                OfficerRewardedEvent => "officer-rewarded",
                OfficerExiledEvent => "officer-exiled",
                CorpsDissolvedEvent => "corps-dissolved",
                CorpsCreatedEvent => "corps-created",
                CorpsChangedEvent => "corps-changed",
                CorpsControlChangedEvent => "corps-control-changed",
                CorpsLeaderChangedEvent => "corps-leader-changed",
                CommanderChangedEvent => "commander-changed",
                PersonnelReturnedEvent => "personnel-returned",
                PersonnelTerritoryTransferredEvent => "personnel-territory-transferred",
                PrisonerReleasedEvent => "prisoner-released",
                OfficeAssignedEvent => "office-assigned",
                TreasureTransferredEvent { Reason: "grant" } => "treasure-granted",
                TreasureTransferredEvent { Reason: "confiscate" } => "treasure-confiscated",
                _ => null
            };
            if (personnel is null) continue;
            _report.PersonnelEvents[personnel] = _report.PersonnelEvents.GetValueOrDefault(personnel) + 1;
            _report.Goal = "G04";
            _report.Scope = "Personnel, corps governance and economic cycles; recorded event counts describe exercised paths, not every G04 acceptance case. No warfare or complete strategic AI.";
        }
        if (_report.MilitaryEvents.Count > 0)
        {
            _report.Goal = "G05";
            _report.Scope = "Military deployment, daily movement and transport/return through public commands; event counts describe exercised paths, not every G05 acceptance case. Combat and complete strategic AI remain pending.";
        }
        if (_report.MilitaryEvents.ContainsKey("battle-damage"))
        {
            _report.Goal = "G06";
            _report.Scope = "Ordinary battles, capture, force elimination and deterministic replay; event counts describe exercised paths. Advanced warfare and complete strategic AI remain pending.";
        }
        if (_report.MilitaryEvents.Keys.Any(key => key.StartsWith("construction-", StringComparison.Ordinal) || key == "tactic-activated"))
        {
            _report.Goal = "G07";
            _report.Scope = "Construction or tactics through public simulation and deterministic replay; event counts describe exercised paths, not complete G07 acceptance.";
        }
        if (_report.DiplomacyStrategyEvents.Count > 0)
        {
            _report.Goal = "G08";
            _report.Scope = "Diplomacy or strategies through public commands, explicit player decisions, save and replay; event counts describe exercised paths, not complete G08 acceptance or full strategic AI.";
        }
    }

    private int Fail(int code, string category, Exception error, int? line = null)
    {
        _report.Success = false;
        _report.TerminationReason = "failure";
        _report.FirstFailure ??= new(category, error.Message + (error.InnerException is null ? "" : " " + error.InnerException.Message), line);
        return code;
    }

    private void Replay(Dictionary<string, string> options)
    {
        var definition = Content(options);
        string log = Path.GetFullPath(Required(options, "log"));
        _report.CommandLog = log;
        var header = CommandLogService.ReadHeader(log);
        _session = options.TryGetValue("load", out string? load) ? SaveService.Load(load, definition) :
            options.TryGetValue("restore", out string? restore) ? SaveService.Read(restore, definition) : GameSession.Start(definition, header.Seed, battleDeathsEnabled: header.BattleDeathsEnabled);
        _started = _session.Snapshot.Clock;
        if (options.ContainsKey("record")) _session.BeginRecording();
        var result = CommandLogService.Replay(log, _session);
        _report.AcceptedOperations = result.Accepted;
        _report.AlreadyProcessedOperations = result.AlreadyProcessed;
        SummarizeEvents(result.Events);
        _report.WaitCommands = result.Attempts.Count(a => a.Disposition == SessionDisposition.Accepted && a.Request?.Operation is CommandOperation { Command: WaitCommand });
        _report.EventLog = Path.ChangeExtension(_reportPath!, ".events.jsonl");
        _report.AttemptLog = Path.ChangeExtension(_reportPath!, ".attempts.jsonl");
        WriteLines(_report.EventLog, result.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)));
        WriteLines(_report.AttemptLog, result.Attempts.Select(a => CanonicalJson.Text(a)));
        if (options.TryGetValue("save", out string? save)) SaveService.Write(save, _session);
        if (options.TryGetValue("record", out string? record)) RecordingService.Write(record, _session.CaptureRecording());
        if (result.FirstFailure is not null)
            throw new CommandLogException(result.FirstFailure.Line, "Operation rejected: " + result.FirstFailure.Rejection);
        _report.TerminationReason = _session.Snapshot.Simulation.TerminalReason ?? "log-complete";
    }

    private void ReplayRecording(Dictionary<string, string> options)
    {
        var replay = RecordingService.Replay(Required(options, "recording"), Content(options));
        _session = replay.Session;
        _started = replay.Started;
        _report.AcceptedOperations = replay.Accepted;
        SummarizeEvents(replay.Events);
        _report.EventLog = Path.ChangeExtension(_reportPath!, ".events.jsonl");
        WriteLines(_report.EventLog, replay.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)));
        if (options.TryGetValue("save", out string? save)) SaveService.Write(save, _session);
        _report.TerminationReason = "recording-complete";
    }

    private void Verify(Dictionary<string, string> options, string output)
    {
        int xuns = options.TryGetValue("xuns", out var value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed :
            options.ContainsKey("xuns") ? throw new UsageException("Invalid xun count.") : 36;
        if (xuns <= 0) throw new UsageException("Xun count must be positive.");
        ulong seed = options.TryGetValue("seed", out value) && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedSeed) ? parsedSeed :
            options.ContainsKey("seed") ? throw new UsageException("Invalid seed.") : 190UL;
        if (options.TryGetValue("strategy", out var strategy) && strategy != "simple") throw new UsageException("Unknown strategy.");
        _report.Policy = options.ContainsKey("strategy") ? "simple" : "scripted";
        var definition = Content(options);
        _session = GameSession.Start(definition, seed);
        _session.BeginRecording();
        _started = _session.Snapshot.Clock;
        var restored = GameSession.Start(definition, seed);
        Directory.CreateDirectory(output);
        SaveService.Write(Path.Combine(output, "initial.save.json"), _session);
        List<string> events = [];
        List<object> checkpoints = [];
        void Compare(SessionRequest request)
        {
            var a = _session.Apply(request);
            var b = restored.Apply(request);
            if (a.Disposition != SessionDisposition.Accepted || b.Disposition != SessionDisposition.Accepted ||
                a.Snapshot.StateHash != b.Snapshot.StateHash ||
                !a.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)).SequenceEqual(b.Events.Select(e => CanonicalJson.Text<DomainEvent>(e))))
                throw new VerificationException($"Operation {request.Sequence} diverged or was rejected.");
            _report.AcceptedOperations++;
            SummarizeEvents(a.Events);
            if (request.Operation is CommandOperation { Command: WaitCommand }) _report.WaitCommands++;
            events.AddRange(a.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)));
        }
        void Checkpoint(string reason)
        {
            SaveService.Write(Path.Combine(output, "checkpoint.save.json"), restored);
            restored = SaveService.Read(Path.Combine(output, "checkpoint.save.json"), definition);
            if (_session.Capture().StateHash != restored.Capture().StateHash || _session.Snapshot.Revision != restored.Snapshot.Revision)
                throw new VerificationException("Restored session differs from continuous session.");
            if (restored.InitialEvents.Count != 0) throw new VerificationException("Restore repeated initial preparation.");
            _report.RestoreCount++;
            checkpoints.Add(new { reason, clock = restored.Snapshot.Clock, operationCount = restored.NextOperationSequence, stateHash = restored.Capture().StateHash });
        }
        Compare(_session.Request(new ChronicleOperation("Headless scripted verification")));
        bool firstCommand = true;
        for (int xun = 0; xun < xuns && _session.Snapshot.Simulation.Boundary != SimulationBoundary.Terminal; xun++)
        {
            foreach (var seat in _session.Snapshot.Simulation.Controls)
            {
                var diplomacyRequest = _session.Request(new EndControlOperation(), seat.ControllerId);
                var diplomacy = DiplomacyPlanner.Choose(_session, seat, diplomacyRequest.Id);
                if (diplomacy is not WaitCommand)
                    Compare(diplomacyRequest with { Operation = new CommandOperation(diplomacy) });
                var strategyRequest = _session.Request(new EndControlOperation(), seat.ControllerId);
                var strategyCommand = StrategyPlanner.Choose(_session, seat, strategyRequest.Id);
                if (strategyCommand is not WaitCommand)
                    Compare(strategyRequest with { Operation = new CommandOperation(strategyCommand) });
                var planned = _session.Request(new EndControlOperation(), seat.ControllerId);
                Compare(planned with { Operation = new CommandOperation(DomesticPlanner.Choose(_session, seat, planned.Id, xun, !options.ContainsKey("strategy"))) });
                if (firstCommand) { Checkpoint("first-command"); firstCommand = false; }
                Compare(_session.Request(new EndControlOperation(), seat.ControllerId));
            }
            Compare(_session.Request(new AdvanceOperation()));
            while (_session.Snapshot.Simulation.Boundary == SimulationBoundary.AwaitingDecision)
            {
                Checkpoint("player-decision");
                var state = _session.Snapshot.Simulation;
                int force = state.PendingTacticStudy?.RespondingForceId ?? state.PendingExecutionSuccession?.RespondingForceId ?? state.PendingDailyTask?.RespondingForceId ?? state.PendingAlliance!.RespondingForceId!.Value;
                var seat = state.Controls.Single(c => c.ForceId == force);
                var request = _session.Request(new EndControlOperation(), seat.ControllerId);
                GameCommand answer;
                if (_session.GetTacticStudyPrompt() is { } study)
                    answer = new ChooseTacticStudentCommand(request.Id, force, state.NextSequence, study.CityId, study.CandidateIds[0]);
                else if (_session.GetExecutionSuccessionPrompt() is { } succession)
                    answer = new ChooseExecutionSuccessorCommand(request.Id, force, state.NextSequence, succession.RulerId, succession.CandidateIds[0]);
                else if (state.PendingDailyTask is { } daily)
                {
                    var task = state.Tasks.Single(t => t.Id == daily.RemainingTaskIds[0]);
                    // The verification policy explicitly declines negotiations; the session never answers for a player.
                    answer = new RespondDailyTaskCommand(request.Id, force, state.NextSequence, task.Id, task.ResolutionStep, false);
                }
                else answer = new AllianceParticipationCommand(request.Id, force, state.NextSequence, state.PendingAlliance!.TargetForceId, true);
                Compare(request with { Operation = new CommandOperation(answer) });
            }
            if (xun % 5 == 0 || _session.Snapshot.Clock is { Month: 12, Day: 21 }) Checkpoint("stable-period");
            if (_session.View.Year != restored.View.Year || _session.View.Day != restored.View.Day ||
                !_session.Records.SequenceEqual(restored.Records) || !_session.Chronicle.SequenceEqual(restored.Chronicle))
                throw new VerificationException("Restored application query or records differ.");
        }
        if (_report.DevelopmentCommands == 0) throw new VerificationException("Scenario executed no development commands.");
        if (_session.Capture().StateHash != restored.Capture().StateHash) throw new VerificationException("Final session hash mismatch.");
        SaveService.Write(Path.Combine(output, "continuous.save.json"), _session);
        SaveService.Write(Path.Combine(output, "restored.save.json"), restored);
        if (!File.ReadAllBytes(Path.Combine(output, "continuous.save.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "restored.save.json"))))
            throw new VerificationException("Canonical save files differ.");
        _report.CommandLog = Path.Combine(output, "commands.jsonl");
        CommandLogService.Write(_report.CommandLog, _session);
        _report.EventLog = Path.Combine(output, "events.jsonl");
        WriteLines(_report.EventLog, events);
        Write(Path.Combine(output, "checkpoints.json"), CanonicalJson.Encode(checkpoints));
        string recordingPath = Path.Combine(output, "verification.recording.json");
        RecordingService.Write(recordingPath, _session.CaptureRecording());
        var recordingReplay = RecordingService.Replay(recordingPath, definition);
        if (recordingReplay.Session.Capture().StateHash != _session.Capture().StateHash)
            throw new VerificationException("Recording replay differs.");
        var replayed = GameSession.Start(definition, seed);
        var replay = CommandLogService.Replay(_report.CommandLog, replayed);
        if (replay.FirstFailure is not null || replayed.Capture().StateHash != _session.Capture().StateHash ||
            !events.SequenceEqual(replay.Events.Select(e => CanonicalJson.Text<DomainEvent>(e))))
            throw new VerificationException("Exported log replay differs.");
        var duplicate = CommandLogService.Replay(_report.CommandLog, restored);
        if (duplicate.FirstFailure is not null || duplicate.AlreadyProcessed != _session.NextOperationSequence || duplicate.Events.Length != 0 ||
            restored.Capture().StateHash != _session.Capture().StateHash)
            throw new VerificationException("Duplicate log prefix changed the session.");
        _report.AlreadyProcessedOperations = duplicate.AlreadyProcessed;
        _report.TerminationReason = _session.Snapshot.Simulation.TerminalReason ?? "requested-xuns";
    }

    private static ScenarioInitialState Content(Dictionary<string, string> options)
    {
        try
        {
            var definition = options.ContainsKey("fixture") ? ScenarioFixture.Create() :
                JsonSerializer.Deserialize<ScenarioInitialState>(File.ReadAllText(Required(options, "scenario"), Encoding.UTF8), ScenarioContentFiles.DataJsonOptions)
                    ?? throw new JsonException("Null scenario.");
            definition.Validate();
            return definition;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
        { throw new ContentException("Invalid scenario content.", error); }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("run" or "verify" or "replay"))
            throw new UsageException("Use verify --fixture|--scenario PATH --output DIR [--xuns N] [--seed N], or run --fixture|--scenario PATH --log PATH --report PATH [--load PATH|--restore PATH] [--save PATH] [--record PATH], or replay --fixture|--scenario PATH --recording PATH --report PATH [--save PATH].");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = args[0] == "verify" ? ["fixture", "scenario", "output", "xuns", "seed", "strategy"] : args[0] == "run" ? ["fixture", "scenario", "log", "report", "load", "restore", "save", "record"] : ["fixture", "scenario", "recording", "report", "save"];
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new UsageException("Expected named option.");
            string key = args[i][2..];
            if (!allowed.Contains(key) || values.ContainsKey(key)) throw new UsageException("Unknown or duplicate option: " + key);
            if (key == "fixture") values.Add(key, "true");
            else
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new UsageException("Missing value: " + key);
                values.Add(key, args[i]);
            }
        }
        if (values.ContainsKey("fixture") == values.ContainsKey("scenario")) throw new UsageException("Select exactly one scenario source.");
        if (values.ContainsKey("load") && values.ContainsKey("restore")) throw new UsageException("Select player load or exact restoration, not both.");
        if (args[0] != "verify") ValidatePaths(values);
        return values;
    }

    private static void ValidatePaths(Dictionary<string, string> options)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var outputs = new Dictionary<string, string>(comparer);
        string report = Path.GetFullPath(Required(options, "report"));
        outputs.Add(report, "report");
        if (!outputs.TryAdd(Path.ChangeExtension(report, ".events.jsonl"), "events") ||
            !outputs.TryAdd(Path.ChangeExtension(report, ".attempts.jsonl"), "attempts"))
            throw new UsageException("Report path conflicts with a diagnostic output.");
        if (options.TryGetValue("save", out var save) && !outputs.TryAdd(Path.GetFullPath(save), "save"))
            throw new UsageException("Output paths must be distinct.");
        if (options.TryGetValue("record", out var record) && !outputs.TryAdd(Path.GetFullPath(record), "record"))
            throw new UsageException("Recording output must be distinct.");
        foreach (string key in new[] { "scenario", "log", "load", "restore", "recording" })
            if (options.TryGetValue(key, out var input) && outputs.TryGetValue(Path.GetFullPath(input), out var output) &&
                !(key is "load" or "restore" && output == "save"))
                throw new UsageException("Output would overwrite an input: " + key);
    }

    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new UsageException("Missing option: " + key);
    private static void WriteLines(string path, IEnumerable<string> lines)
    {
        string text = string.Join('\n', lines);
        Write(path, Encoding.UTF8.GetBytes(text.Length == 0 ? text : text + "\n"));
    }
    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
    }
    private sealed class UsageException(string message) : Exception(message);
    private sealed class ContentException(string message, Exception inner) : Exception(message, inner);
    private sealed class VerificationException(string message) : Exception(message);
}
