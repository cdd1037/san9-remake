using San9.Application;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace San9.Headless.Tests;

public sealed class HeadlessProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-cli-" + Guid.NewGuid().ToString("N"));
    public HeadlessProcessTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(_directory, recursive: true);
    }
    private string FilePath(string name) => Path.Combine(_directory, name);

    [Theory]
    [InlineData(190, 65536)]
    [InlineData(42, 16)]
    public async Task AutonomousPolicyReportsCommittedWorkAndReplaysFromItsSavedInitialState(int seed, int budget)
    {
        var verified = await Run("verify", "--fixture", "--strategy", "autonomous", "--xuns", "6",
            "--seed", seed.ToString(), "--ai-budget", budget.ToString(), "--output", _directory);
        Assert.Equal(0, verified.Code);
        Assert.Equal("AI-06", verified.Report.GetProperty("goal").GetString());
        Assert.Equal(6, verified.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(budget, verified.Report.GetProperty("aiConfiguration").GetProperty("workBudget").GetInt64());
        var turns = verified.Report.GetProperty("aiTurns").EnumerateArray().ToArray();
        Assert.Equal(12, turns.Length);
        Assert.All(turns, turn =>
        {
            long work = turn.GetProperty("work").GetInt64();
            Assert.InRange(work, 0, budget);
            Assert.Equal(work, turn.GetProperty("workByKind").EnumerateObject().Sum(p => p.Value.GetInt64()));
        });
        Assert.Equal(7, verified.Report.GetProperty("aiCheckpoints").GetArrayLength());
        var initial = StrictJson.Deserialize<SaveEnvelope>(File.ReadAllBytes(FilePath("initial.save.json"))).Session;
        Assert.All(initial.World.Simulation.Controls, seat => Assert.Equal(ControllerKind.Automatic, seat.Kind));
        Assert.Equal(0, initial.World.Simulation.ComputerDamageBonusPercent);
        foreach (var force in verified.Report.GetProperty("aiWorkByForce").EnumerateObject())
            Assert.Equal(6, force.Value.GetProperty("turns").GetInt64());
        var logReplay = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("initial.save.json"),
            "--report", FilePath("autonomous-log.json"));
        Assert.Equal(0, logReplay.Code);
        Assert.Equal(verified.Report.GetProperty("finalHash").GetString(), logReplay.Report.GetProperty("finalHash").GetString());
        var freshReplay = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("autonomous-fresh.json"));
        Assert.Equal(0, freshReplay.Code);
        Assert.Equal(verified.Report.GetProperty("finalHash").GetString(), freshReplay.Report.GetProperty("finalHash").GetString());
        var recordingReplay = await Run("replay", "--fixture", "--recording", FilePath("verification.recording.json"),
            "--report", FilePath("autonomous-recording.json"));
        Assert.Equal(0, recordingReplay.Code);
        Assert.Equal(verified.Report.GetProperty("finalHash").GetString(), recordingReplay.Report.GetProperty("finalHash").GetString());
    }

    [Fact]
    public async Task InvalidEvaluationBudgetIsAUsageFailure()
    {
        var result = await Run("verify", "--fixture", "--strategy", "autonomous", "--ai-budget", "0", "--output", _directory);
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingLoggedPolicyCannotSilentlyUseTheDefault()
    {
        WriteFailureScenario();
        var lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var header = JsonNode.Parse(lines[0])!;
        header["aiConfiguration"] = null;
        lines[0] = header.ToJsonString();
        File.WriteAllLines(FilePath("invalid-policy.jsonl"), lines);
        var result = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("invalid-policy.jsonl"),
            "--report", FilePath("invalid-policy-report.json"));
        Assert.Equal(5, result.Code);
        Assert.Equal(1, result.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
    }

    private void WriteFailureScenario()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "failure-cli", 190, 1, 1, 1,
            [new(1, "force", 1, 100) { LeaderId = 1 }],
            [new(1, "home", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
        {
            Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })]
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
        File.WriteAllBytes(FilePath("scenario.json"), CanonicalJson.Encode(definition));
        var session = GameSession.Start(definition, 190);
        SaveService.Write(FilePath("initial.save.json"), session);
        session.BeginRecording();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new ChronicleOperation("accepted prefix"))).Disposition);
        CommandLogService.Write(FilePath("commands.jsonl"), session);
    }

    [Fact]
    public async Task CommandLogCreatesReplayWithRecordedComputerDamageBonus()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "bonus-cli", 190, 1, 1, 1,
            [new(1, "force", 0, 100) { LeaderId = 1 }],
            [new(101, "city", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Officers = [new("ruler", new(1, 1, 101, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler })] , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
        string scenario = FilePath("bonus-scenario.json"), log = FilePath("bonus.commands.jsonl");
        File.WriteAllBytes(scenario, CanonicalJson.Encode(content));
        var session = GameSession.Start(content, 190, 100);
        session.BeginRecording();
        session.AdvanceXun();
        CommandLogService.Write(log, session);
        var result = await Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("bonus-report.json"));
        Assert.Equal(0, result.Code);
        Assert.Equal(session.Capture().StateHash, result.Report.GetProperty("finalHash").GetString());
    }
    [Fact]
    public async Task SearchDiscoveryRecordsRealAnswerAndResumesBeforeSaving()
    {
        var content = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "search-choice-cli", 190, 1, 1, 1, [new(1, "force", 0, 100) { LeaderId = 1 }],
            [new(101, "city", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
        {
            Officers = [new("ruler", new(1, 1, 101, new(80, 80, 80, 80), false)),
                new("searcher", new(2, 1, 101, new(50, 50, 500, 50), false)),
                new("hidden", new(3, null, 101, new(50, 50, 50, 50), false)
                { Status = San9.Core.Domain.OfficerStatus.Undiscovered })]
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
        string scenario = FilePath("search-scenario.json"), log = FilePath("search.commands.jsonl"), recording = FilePath("search.recording.json");
        File.WriteAllBytes(scenario, San9.Core.Simulation.CanonicalJson.Encode(content));
        var session = San9.Application.GameSession.Start(content, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new San9.Core.Simulation.SearchCommand(Guid.NewGuid(), 1, 0, 2, 101)).Submission.Accepted);
        session.AdvanceXun();
        var prompt = session.GetSearchRecruitmentPrompt()!;
        Assert.NotNull(prompt);
        Assert.True(session.Execute(new("player"), new San9.Core.Simulation.RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, false)).Submission.Accepted);
        San9.Application.Persistence.CommandLogService.Write(log, session);
        var executed = await Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("search-run.json"),
            "--save", FilePath("search.save.json"), "--record", recording);
        Assert.Equal(0, executed.Code);
        Assert.Equal(session.Capture().StateHash, executed.Report.GetProperty("finalHash").GetString());
        var restored = San9.Application.Persistence.SaveService.Read(FilePath("search.save.json"), content);
        Assert.Equal(San9.Core.Domain.OfficerStatus.Free, restored.Snapshot.Officers.Single(o => o.Id == 3).Status);
        Assert.Equal(new San9.Core.Simulation.GameClock(190, 1, 11), restored.Snapshot.Clock);
        var replay = await Run("replay", "--scenario", scenario, "--recording", recording, "--report", FilePath("search-replay.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal(session.Capture().StateHash, replay.Report.GetProperty("finalHash").GetString());
    }

    [Theory]
    [InlineData("community")] [InlineData("conflict")] [InlineData("merchant")]
    public async Task TypedExplorationChoicesRunFromExactSaveAndReplayInSeparateProcesses(string kind)
    {
        SearchEventDefinition rule = kind switch
        {
            "community" => new CommemorationSearchEventDefinition([new(101, null, CommunityAbility.Intelligence, [], new("memorial.101"))], 1500, 2, 2)
                { Id = "memorial", Consumption = EventConsumption.OnSuccess, ChanceAbility = CommunityAbility.Politics, ChanceDivisor = 1 },
            "conflict" => new MoneyDemandSearchEventDefinition([3], 100, 2, 100)
                { Id = "demand", ChanceAbility = ConflictChanceAbility.Politics, ChanceDivisor = 1 },
            _ => new MerchantInvestmentDefinition([new("trade", [101], ResourceKind.Money, new("trade.banned"))], 1, 50, 1000, 100, 5, 20, 3, 2, 4, 5)
                { Id = "merchant" }
        };
        var d = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "g10-choice-cli", 190, 1, 1, 1,
            [new(1, "force", 0, 100) { LeaderId = 1, BaseTrust = 100 }],
            [new(101, "city", 1, 0, 0, 1000, 100, 100, 50, [])], [], [])
        {
            Officers = [new("ruler", new(1, 1, 101, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler }),
                new("actor", new(2, 1, 101, new(50, 50, 100, 50), false)),
                new("visitor", new(3, null, null, default, false) { Status = OfficerStatus.Special })],
            SearchEvents = [rule]
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
        string scenario = FilePath("g10.json"), log = FilePath("g10.commands.jsonl"), initial = FilePath("g10.initial.json"), record = FilePath("g10.recording.json");
        File.WriteAllBytes(scenario, JsonSerializer.SerializeToUtf8Bytes(d, San9.Content.Packages.ScenarioContentFiles.DataJsonOptions));
        var session = GameSession.Start(d, 190);
        SaveService.Write(initial, session); session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, 2, 101)).Submission.Accepted);
        session.AdvanceXun();
        var task = session.Snapshot.Simulation.Tasks.Single(t => t.Id == session.Snapshot.Simulation.PendingDailyTask!.RemainingTaskIds[0]);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, task.ResolutionStep, true);
        answer = kind switch
        {
            "community" => answer with { CommunitySearch = CommunitySearchChoice.Accept },
            "conflict" => answer with { SearchConflict = SearchConflictChoice.Pay },
            _ => answer with { MerchantInvestment = new(MerchantInvestmentChoice.Invest, 100) }
        };
        Assert.True(session.Execute(new("player"), answer).Submission.Accepted);
        CommandLogService.Write(log, session);
        var executed = await Run("run", "--scenario", scenario, "--restore", initial, "--log", log,
            "--report", FilePath("g10-run.json"), "--save", FilePath("g10-final.json"), "--record", record);
        Assert.True(executed.Code == 0, executed.Report.ToString()); Assert.Equal(session.Capture().StateHash, executed.Report.GetProperty("finalHash").GetString());
        var replay = await Run("replay", "--scenario", scenario, "--recording", record, "--report", FilePath("g10-replay.json"));
        Assert.Equal(0, replay.Code); Assert.Equal(session.Capture().StateHash, replay.Report.GetProperty("finalHash").GetString());
    }

    [Fact]
    public async Task VictoryEndingCanBeSelectedSavedAndReplayedByStandaloneCli()
    {
        var content = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "ending-cli", 220, 1, 1, 9, [new(9, "force", 0, 500) { LeaderId = 17 }],
            [new(101, "city", 9, 0, 0, 1000, 100, 100, 50, [])], [], [])
        {
            Officers = [new("ruler", new(17, 9, 101, new(80, 80, 80, 80), false))],
            Terminal = new([101], 350), Ending = new([101], [101], [101], [101], [101], [101], [])
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(9, 9, 17, new(1000, 1000)) { IsRoot = true }], []) };
        string scenario = FilePath("ending-scenario.json"), log = FilePath("ending.commands.jsonl"), recording = FilePath("ending.recording.json");
        File.WriteAllBytes(scenario, San9.Core.Simulation.CanonicalJson.Encode(content));
        var session = San9.Application.GameSession.Start(content, 1);
        session.BeginRecording();
        var request = session.Request(new San9.Application.EndingOperation(new(San9.Core.Domain.EndingPolicy.DomesticProsperity, null)));
        Assert.Equal(San9.Application.SessionDisposition.Accepted, session.Apply(request).Disposition);
        San9.Application.Persistence.CommandLogService.Write(log, session);
        var executed = await Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("ending-run.json"),
            "--save", FilePath("ending.save.json"), "--record", recording);
        Assert.Equal(0, executed.Code);
        Assert.Equal(session.Capture().StateHash, executed.Report.GetProperty("finalHash").GetString());
        Assert.Equal((int)San9.Core.Domain.EndingOutcome.FallenState, executed.Report.GetProperty("ending").GetProperty("outcome").GetInt32());
        var replay = await Run("replay", "--scenario", scenario, "--recording", recording, "--report", FilePath("ending-replay.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal(session.Capture().StateHash, replay.Report.GetProperty("finalHash").GetString());
        Assert.Equal(session.Ending, San9.Application.Persistence.SaveService.Read(FilePath("ending.save.json"), content).Ending);
    }

    [Fact]
    public async Task AutomaticDiplomacyRecordsAnswersAndSavesAfterResolution()
    {
        var definition = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "diplomacy-cli", 190, 1, 1, 1,
            [new(1, "player", 1, 100) { LeaderId = 1 }, new(2, "neighbor", 2, 100) { LeaderId = 3 }],
            [new(1, "home", 1, 0, 0, 100, 100, 100, 50, []) { Commerce = 100, MaxCommerce = 500 },
                new(2, "away", 2, 2, 0, 100, 100, 100, 50, []) { Commerce = 100, MaxCommerce = 500 }], [], [])
        {
            Officers = [new("ruler", new(1, 1, 1, new(50, 50, 50, 50), false)),
                new("worker", new(2, 1, 1, new(50, 50, 100, 50), false)),
                new("ruler2", new(3, 2, 2, new(50, 50, 50, 50), false)),
                new("envoy", new(4, 2, 2, new(50, 50, 100, 50), false))],
            Information = San9.Content.Scenarios.InformationDefinitions.Empty with { Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 3, new(30000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 60)] }
        };
        File.WriteAllBytes(FilePath("scenario.json"), San9.Core.Simulation.CanonicalJson.Encode(definition));
        var session = GameSession.Start(definition, 190, aiPolicy: San9.Application.AI.AiPolicyConfiguration.Standard with { WorkBudget = 1 });
        SaveService.Write(FilePath("initial.json"), session);
        session.BeginRecording();
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 4, 1,
            new(GiftKind.Money, 2000, null))).Submission.Accepted);
        session.AdvanceXun();
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        Assert.True(session.Execute(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, pending.RemainingTaskIds[0], 0, false)).Submission.Accepted);
        CommandLogService.Write(FilePath("commands.jsonl"), session);
        var result = await Run("run", "--scenario", FilePath("scenario.json"), "--restore", FilePath("initial.json"),
            "--log", FilePath("commands.jsonl"), "--save", FilePath("resolved.json"),
            "--record", FilePath("recording.json"), "--report", FilePath("report.json"));
        Assert.Equal(0, result.Code);
        Assert.True(result.Report.GetProperty("diplomacyStrategyEvents").GetProperty("gift-resolved").GetInt32() > 0);
        Assert.Contains("respond-daily-task", File.ReadAllText(FilePath("commands.jsonl")));
        Assert.Equal(session.Capture().StateHash, SaveService.Read(FilePath("resolved.json"), definition).Capture().StateHash);
        var replay = await Run("replay", "--scenario", FilePath("scenario.json"), "--recording", FilePath("recording.json"), "--report", FilePath("replay.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal(session.Capture().StateHash, replay.Report.GetProperty("finalHash").GetString());
    }

    [Fact]
    public async Task MilitaryRecordingReportsDeploymentMovementAndSingleAbsorption()
    {
        var definition = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "military-cli", 190, 1, 1, 1, [new(1, "force", 1, 100)],
            [new(1, "home", 1, 0, 0, 1000, 100, 100, 50, []), new(2, "destination", 1, 2, 0, 100, 100, 100, 50, [])], [], [])
        {
            Officers = [new("person", new(1, 1, 1, new(60, 60, 60, 60), false))],
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)],
                [new(1, 10, 0, 10000, 5, new(1, 1), San9.Core.Domain.TravelMode.Land)],
                [new(1, new(2, 2), new(1, 1)), new(2, new(12, 2), new(1, 1))]) { TransportFormations = new(1, null) }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }], []) };
        var session = San9.Application.GameSession.Start(definition, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new San9.Core.Simulation.TransportUnitCommand(Guid.NewGuid(), 1, 0,
            1, [1], 400, 2, 20, 30)).Submission.Accepted);
        session.AdvanceXun();
        File.WriteAllBytes(FilePath("scenario.json"), San9.Core.Simulation.CanonicalJson.Encode(definition));
        San9.Application.Persistence.RecordingService.Write(FilePath("military.recording.json"), session.EndRecording());
        var result = await Run("replay", "--scenario", FilePath("scenario.json"), "--recording", FilePath("military.recording.json"),
            "--report", FilePath("military-report.json"));
        Assert.Equal(0, result.Code);
        Assert.Equal("G05", result.Report.GetProperty("goal").GetString());
        Assert.Equal(1, result.Report.GetProperty("militaryEvents").GetProperty("unit-deployed").GetInt32());
        Assert.Equal(1, result.Report.GetProperty("militaryEvents").GetProperty("unit-absorbed").GetInt32());
        Assert.True(result.Report.GetProperty("militaryEvents").GetProperty("unit-moved").GetInt32() > 0);
        Assert.Equal(session.Capture().StateHash, result.Report.GetProperty("finalHash").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EconomicYearUsesAllProjectsAndSurvivesDiskRestore(bool policy)
    {
        string[] args = policy ? ["verify", "--fixture", "--strategy", "simple", "--xuns", "36", "--output", _directory] :
            ["verify", "--fixture", "--xuns", "36", "--output", _directory];
        var result = await Run(args);
        Assert.Equal(0, result.Code);
        Assert.Equal(36, result.Report.GetProperty("completedXun").GetInt32());
        foreach (string activity in new[] { "Patrol", "Commerce", "Cultivation", "Repair", "Recruit", "Train" })
            Assert.True(result.Report.GetProperty("activities").GetProperty(activity).GetInt32() > 0);
        Assert.Equal(0, result.Report.GetProperty("waitCommands").GetInt32());
        Assert.Equal(0, result.Report.GetProperty("resourceShortfall").GetInt64());
        Assert.Equal(10, result.Report.GetProperty("restoreCount").GetInt32());
        Assert.Equal(File.ReadAllBytes(FilePath("continuous.save.json")), File.ReadAllBytes(FilePath("restored.save.json")));
    }

    [Fact]
    public async Task PersonnelCommandsAndReplayReportTheirActualScope()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "personnel-cli", 190, 1, 1, 1,
            [new(1, "force", 1, 50) { LeaderId = 1 }],
            [new(1, "home", 1, 0, 0, 100, 100, 100, 50, [])], [], [])
        {
            Officers = [new("ruler", new(1, 1, 1, new(60, 70, 80, 90), false) { Status = OfficerStatus.Ruler }),
                new("officer", new(2, 1, 1, new(50, 60, 100, 70), false)),
                new("adviser", new(3, 1, 1, new(40, 80, 70, 60), false))]
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 20000)) { IsRoot = true }], []) };
        string scenario = FilePath("personnel.scenario.json");
        File.WriteAllBytes(scenario, CanonicalJson.Encode(definition));
        var session = GameSession.Start(definition, 190);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new RewardOfficerCommand(Guid.NewGuid(), 1, 0, 2)).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 1, 3)).Submission.Accepted);
        CommandLogService.Write(FilePath("personnel.jsonl"), session);
        var run = await Run("run", "--scenario", scenario, "--log", FilePath("personnel.jsonl"), "--report", FilePath("personnel-report.json"),
            "--record", FilePath("personnel.recording.json"));
        Assert.Equal(0, run.Code);
        Assert.Equal(session.Capture().StateHash, run.Report.GetProperty("finalHash").GetString());
        Assert.Equal("G04", run.Report.GetProperty("goal").GetString());
        Assert.Equal(1, run.Report.GetProperty("personnelEvents").GetProperty("officer-rewarded").GetInt32());
        Assert.Equal(1, run.Report.GetProperty("personnelEvents").GetProperty("adviser-appointed").GetInt32());
        var replay = await Run("replay", "--scenario", scenario, "--recording", FilePath("personnel.recording.json"), "--report", FilePath("personnel-replay-report.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal("G04", replay.Report.GetProperty("goal").GetString());
        Assert.Equal(run.Report.GetProperty("finalHash").GetString(), replay.Report.GetProperty("finalHash").GetString());
    }

    [Fact]
    public async Task ExecutableRunsCommandsSavesResumesAndSkipsRepeatedPrefix()
    {
        var verification = await Run("verify", "--fixture", "--xuns", "2", "--output", _directory);
        Assert.Equal(0, verification.Code);
        Assert.True(verification.Report.GetProperty("success").GetBoolean());
        Assert.Equal(2, verification.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(4, verification.Report.GetProperty("developmentCommands").GetInt32());
        var readOptions = new JsonSerializerOptions(CanonicalJson.Options) { AllowOutOfOrderMetadataProperties = true };
        var requests = File.ReadLines(FilePath("commands.jsonl")).Skip(1)
            .Select(line => JsonSerializer.Deserialize<SessionRequest>(line, readOptions)!).ToArray();
        Assert.Contains(requests, request => request.ControllerId.StartsWith("ai:", StringComparison.Ordinal) && request.Operation is EndControlOperation);
        Assert.DoesNotContain(requests, request => request.ControllerId.StartsWith("ai:", StringComparison.Ordinal) && request.Operation is CommandOperation);
        string finalHash = verification.Report.GetProperty("finalHash").GetString()!;
        var replay = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("initial.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("replay-report.json"));
        Assert.Equal(0, replay.Code);
        Assert.Equal(finalHash, replay.Report.GetProperty("finalHash").GetString());
        Assert.Equal(File.ReadAllBytes(FilePath("events.jsonl")), File.ReadAllBytes(FilePath("replay-report.events.jsonl")));
        var repeated = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("replay.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("duplicate-report.json"));
        Assert.Equal(0, repeated.Code);
        Assert.Equal(0, repeated.Report.GetProperty("acceptedOperations").GetInt32());
        Assert.Equal(requests.Length, repeated.Report.GetProperty("alreadyProcessedOperations").GetInt32());
        Assert.Equal(finalHash, repeated.Report.GetProperty("finalHash").GetString());
        Assert.Equal(0, new FileInfo(FilePath("duplicate-report.events.jsonl")).Length);
    }

    [Fact]
    public async Task ExplicitRecordingAndPlaybackUseSeparateArtifacts()
    {
        Assert.Equal(0, (await Run("verify", "--fixture", "--xuns", "2", "--output", _directory)).Code);
        var run = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("record-report.json"),
            "--save", FilePath("player.save.json"), "--record", FilePath("debug.recording.json"));
        Assert.Equal(0, run.Code);
        var playback = await Run("replay", "--fixture", "--recording", FilePath("debug.recording.json"),
            "--report", FilePath("playback-report.json"), "--save", FilePath("playback.save.json"));
        Assert.Equal(0, playback.Code);
        Assert.Equal(2, playback.Report.GetProperty("completedXun").GetInt32());
        Assert.Equal(run.Report.GetProperty("finalHash").GetString(), playback.Report.GetProperty("finalHash").GetString());
        Assert.Equal(File.ReadAllBytes(FilePath("player.save.json")), File.ReadAllBytes(FilePath("playback.save.json")));
        Assert.Equal(File.ReadAllBytes(FilePath("record-report.events.jsonl")), File.ReadAllBytes(FilePath("playback-report.events.jsonl")));
        Assert.DoesNotContain("operations", File.ReadAllText(FilePath("player.save.json")));
        var midway = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("checkpoint.save.json"),
            "--report", FilePath("midway-report.json"), "--record", FilePath("midway.recording.json"));
        Assert.Equal(0, midway.Code);
        Assert.Equal(1, midway.Report.GetProperty("completedXun").GetInt32());
        Assert.True(midway.Report.GetProperty("alreadyProcessedOperations").GetInt32() > 0);
        var midPlayback = await Run("replay", "--fixture", "--recording", FilePath("midway.recording.json"), "--report", FilePath("mid-playback-report.json"));
        Assert.Equal(0, midPlayback.Code);
        Assert.Equal(1, midPlayback.Report.GetProperty("completedXun").GetInt32());
        Assert.NotEqual(run.Report.GetProperty("finalHash").GetString(), midway.Report.GetProperty("finalHash").GetString());
        Assert.Equal(midway.Report.GetProperty("finalHash").GetString(), midPlayback.Report.GetProperty("finalHash").GetString());
        var mixed = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"),
            "--load", FilePath("checkpoint.save.json"), "--restore", FilePath("checkpoint.save.json"),
            "--report", FilePath("mixed-report.json"));
        Assert.Equal(2, mixed.Code);
        var conflict = await Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("conflict.json"), "--record", FilePath("commands.jsonl"));
        Assert.Equal(2, conflict.Code);
        var node = JsonNode.Parse(File.ReadAllBytes(FilePath("debug.recording.json")))!;
        node["formatVersion"] = 999;
        File.WriteAllText(FilePath("bad.recording.json"), node.ToJsonString());
        var bad = await Run("replay", "--fixture", "--recording", FilePath("bad.recording.json"), "--report", FilePath("bad-record-report.json"));
        Assert.Equal(5, bad.Code);
        Assert.Equal("recording", bad.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MalformedLogReportsFailureLineAndKeepsAcceptedProgress()
    {
        WriteFailureScenario();
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        File.WriteAllLines(FilePath("bad.jsonl"), [lines[0], lines[1], "{broken}"]);
        var result = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("bad.jsonl"), "--save", FilePath("partial.save.json"),
            "--report", FilePath("bad-report.json"));
        Assert.Equal(5, result.Code);
        Assert.False(result.Report.GetProperty("success").GetBoolean());
        Assert.Equal(3, result.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
        Assert.Equal(1, result.Report.GetProperty("acceptedOperations").GetInt32());
        using var save = JsonDocument.Parse(File.ReadAllBytes(FilePath("partial.save.json")));
        Assert.Equal(1, save.RootElement.GetProperty("session").GetProperty("chronicle").GetArrayLength());
        Assert.True(File.Exists(FilePath("bad-report.json")));
    }

    [Fact]
    public async Task OlderRulesWarnInReportAndStandardErrorWhileContinuing()
    {
        WriteFailureScenario();
        var save = JsonNode.Parse(File.ReadAllBytes(FilePath("initial.save.json")))!;
        save["rulesVersion"] = "older-save-rules";
        File.WriteAllText(FilePath("older.save.json"), save.ToJsonString(), new System.Text.UTF8Encoding(false));
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var header = JsonNode.Parse(lines[0])!;
        header["rulesVersion"] = "older-log-rules";
        lines[0] = header.ToJsonString();
        File.WriteAllLines(FilePath("older.jsonl"), lines, new System.Text.UTF8Encoding(false));
        var result = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("older.jsonl"),
            "--restore", FilePath("older.save.json"), "--report", FilePath("warnings.json"));
        Assert.Equal(0, result.Code);
        Assert.True(result.Report.GetProperty("success").GetBoolean());
        Assert.Equal(1, result.Report.GetProperty("acceptedOperations").GetInt32());
        Assert.Equal(new[] { "older-save-rules", "older-log-rules" }, result.Report.GetProperty("rulesVersionWarnings")
            .EnumerateArray().Select(w => w.GetProperty("savedVersion").GetString()));
    }

    [Fact]
    public async Task UnknownSaveVersionHasSpecificFailureCode()
    {
        WriteFailureScenario();
        var save = JsonNode.Parse(File.ReadAllBytes(FilePath("initial.save.json")))!;
        save["formatVersion"] = 999;
        File.WriteAllText(FilePath("old.save.json"), save.ToJsonString());
        var result = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"), "--restore", FilePath("old.save.json"),
            "--report", FilePath("old-report.json"));
        Assert.Equal(4, result.Code);
        Assert.Equal("save-UnsupportedVersion", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownCommandAndConflictingPathsAreRejected()
    {
        WriteFailureScenario();
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var request = JsonNode.Parse(lines[1])!;
        request["operation"]!["type"] = "teleport";
        File.WriteAllLines(FilePath("unknown.jsonl"), [lines[0], request.ToJsonString()]);
        var unknown = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("unknown.jsonl"), "--report", FilePath("unknown-report.json"));
        Assert.Equal(5, unknown.Code);
        Assert.Equal(2, unknown.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
        byte[] before = File.ReadAllBytes(FilePath("commands.jsonl"));
        var conflict = await Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"), "--report", FilePath("commands.jsonl"));
        Assert.Equal(2, conflict.Code);
        Assert.Equal(before, File.ReadAllBytes(FilePath("commands.jsonl")));
    }

    [Fact]
    public async Task UsageFailureStillWritesMachineReadableStandardOutput()
    {
        var result = await Run("unsupported");
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    private static async Task<(int Code, JsonElement Report)> Run(params string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "San9Remake.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string assembly = Path.Combine(root.FullName, "src", "San9.Headless", "bin", configuration, "net10.0", "San9.Headless.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(assembly);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        string output = await stdout;
        string errors = await stderr;
        using var document = JsonDocument.Parse(output);
        string expectedErrors = string.Concat(document.RootElement.GetProperty("rulesVersionWarnings")
            .EnumerateArray().Select(w => w.GetProperty("message").GetString() + Environment.NewLine));
        Assert.Equal(expectedErrors, errors);
        return (process.ExitCode, document.RootElement.Clone());
    }
}
