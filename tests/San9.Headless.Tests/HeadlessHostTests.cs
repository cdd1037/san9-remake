using San9.Tests;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Content.Packages;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Headless;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace San9.Headless.Tests;

public sealed class HeadlessHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-host-" + Guid.NewGuid().ToString("N"));
    public HeadlessHostTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(_directory, recursive: true);
    }
    private string FilePath(string name) => Path.Combine(_directory, name);
    private SessionSnapshot ReadSession(string name) => StrictJson.Deserialize<SaveEnvelope>(File.ReadAllBytes(FilePath(name))).Session;

    [Fact]
    public void CommandScriptRestoresAuthoredWorldFromAnExplicitStartingSave()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "authored-start", 190, 1, 1, 1,
            [new(1, "old", 5, 100) { LeaderId = 10 }],
            [new(1, "owned", 1, 0, 0, 10000, 100, 100, 50, []) { Kind = SettlementKind.City },
             new(2, "empty", null, 1, 0, 10000, 100, 100, 50, []) { Kind = SettlementKind.City }], [], [])
        {
            Officers = [new("old.ruler", new(10, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Information = new([], [], [], [new(1, 1, 10, new(1000, 10000)) { IsRoot = true }], []),
            ForceEmergenceEnabled = false, ForceConsolidationEnabled = false, CityIndependenceEnabled = false
        };
        var person = new San9.Content.Authoring.CreatedOfficer(Guid.NewGuid(), "新君主") { BirthYear = 160, AppearanceYear = 180 };
        var setup = new San9.Content.Authoring.NewGameSetup
        {
            Library = San9.Content.Authoring.CreatedOfficerLibrary.Empty with { Officers = [person] }, Forces = [new(person.Id, 2)]
        };
        var session = GameSession.Start(definition, 42, playerForceIds: [2], setup: setup);
        File.WriteAllBytes(FilePath("scenario.json"), CanonicalJson.Encode(definition));
        CommandLogService.Write(FilePath("commands.jsonl"), CommandLogService.CreateHeader(session), []);
        SaveService.Write(FilePath("start.save.json"), session);
        var missingStart = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"),
            "--report", FilePath("missing-start.json"));
        Assert.Equal(2, missingStart.Code);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--restore", FilePath("start.save.json"), "--log", FilePath("commands.jsonl"),
            "--report", FilePath("report.json"), "--save", FilePath("authored.save.json"));
        Assert.True(result.Code == 0, result.Report.ToString());
        var actual = ReadSession("authored.save.json");
        Assert.Equal(person.Name, Assert.Single(actual.Definitions.Officers).Name);
        StateAssert.Equal(session.Capture(), actual);
    }

    [Fact]
    public async Task PublishedPackageUsesDeclaredResourceRootAndExplicitPlayerSelection()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "custom-package", 281, 1, 1, null,
            [new(73, "force", 1, 50)], [new(1, "city", 73, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([], [], [], [new(73, 73, null, new(1000, 1000)) { IsRoot = true }], []) };
        string package = FilePath("scenarios/custom");
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("unused"),
            new(new(MapDefinition.CurrentSchemaVersion, "fixture", 1, 1, 1, 1, [], [], [], [], [], [], [], [], []), []),
            definition, new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, []), []);
        await ScenarioContentFiles.WriteAsync(package, content, resourceRoot: _directory);
        var session = GameSession.Start(definition with { PlayerForceId = 73 }, 42);
        CommandLogService.Write(FilePath("commands.jsonl"), CommandLogService.CreateHeader(session), []);
        var result = Run("run", "--content-package", package, "--player-force", "73", "--log", FilePath("commands.jsonl"),
            "--report", FilePath("report.json"), "--save", FilePath("result.save.json"));
        Assert.True(result.Code == 0, result.Report.ToString());
        var state = ReadSession("result.save.json");
        Assert.Equal(281, state.World.Clock.Year);
        Assert.Contains(state.World.Simulation.Controls, seat => seat.ForceId == 73 && seat.Kind == ControllerKind.Player);
    }

    [Fact]
    public void CommandScriptRestoresCompanionConfigurationAndRunsPlayerDomesticWork()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "player-domestic", 190, 1, 1, 1,
            [new(1, "force", 1, 50) { LeaderId = 1 }],
            [new(1, "city", 1, 0, 0, 100, 100, 100, 50, [])], [], [])
        {
            Officers = [new("officer", new(1, 1, 1, new(100, 100, 100, 100), false))],
            Information = new([], [], [], [new(1, 1, 1, new(1000, 2000)) { IsRoot = true }], []),
        };
        File.WriteAllBytes(FilePath("scenario.json"), CanonicalJson.Encode(definition));
        var session = GameSession.Start(definition, 42, companionSettings: new()
        {
            PlayerAutoDomesticEnabled = true, ConfigureTacticsOnFormationSelection = true,
            DisableComputerRewardTimingRestrictions = true,
            BattleDeathProtectionThreshold = 125, AllowCommanderBattleDeaths = true, DuelDeathCoefficient = 5,
            DisablePlayerPrisonerExecution = true, DisableComputerPrisonerExecution = false,
            InfiniteLifespan = true,
            ComputerDiplomacyPermissions = new() { Gift = false, RequestAttack = false, Surrender = false },
            PlayerDelegatedDiplomacyPermissions = new() { ReturnPrisoner = false },
            ComputerCorpsOptions = new() { DomesticDelegationRecruitment = true, CrossCorpsTransport = false },
            PlayerDelegatedCorpsOptions = new() { CrossCorpsRelief = false },
        });
        var header = CommandLogService.CreateHeader(session);
        var end = session.Request(new EndControlOperation());
        Assert.Equal(SessionDisposition.Accepted, session.Apply(end).Disposition);
        CommandLogService.Write(FilePath("commands.jsonl"), header, [end]);
        Assert.Equal(session.Companion, CommandLogService.ReadHeader(FilePath("commands.jsonl")).Companion);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"),
            "--report", FilePath("report.json"), "--save", FilePath("result.save.json"));
        Assert.True(result.Code == 0, result.Report.ToString());
        var saved = ReadSession("result.save.json");
        StateAssert.Equal(session.Capture(), saved);
        Assert.Equal(DomesticActivity.Train, Assert.Single(saved.Records).Activity);
        Assert.True(session.Companion.PlayerAutoDomesticEnabled);
    }

    [Fact]
    public void DefaultRunDoesNotWriteAutomaticSavesAndOnlyExplicitSaveWritesAFile()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "no-auto-save", 190, 1, 1, 1,
            [new(1, "force", 1, 50)], [new(1, "city", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([], [], [], [new(1, 1, null, new(1000, 1000)) { IsRoot = true }], []) };
        File.WriteAllBytes(FilePath("scenario.json"), CanonicalJson.Encode(definition));
        var session = GameSession.Start(definition, 42);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        for (int i = 0; i < 6; i++) AdvanceInput();
        CommandLogService.Write(FilePath("commands.jsonl"), inputHeader, requests);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"), "--report", FilePath("report.json"));
        Assert.Equal(0, result.Code);
        Assert.Equal(new[] { "commands.jsonl", "report.attempts.jsonl", "report.events.jsonl", "report.json", "scenario.json" },
            Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order().ToArray());
        result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"),
            "--report", FilePath("report.json"), "--save", FilePath("explicit.save.json"));
        Assert.Equal(0, result.Code);
        Assert.Single(Directory.GetFiles(_directory, "*.save.json", SearchOption.AllDirectories));
        Assert.Equal(60, ReadSession("explicit.save.json").World.Clock.DayNumber - new GameClock(190, 1, 1).DayNumber);
    }

    [Fact]
    public void VerificationComparesPendingPlayerDecisionBeforeRespondingAndSaving()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "pending-verification", 190, 1, 1, 1,
            [new(1, "force", 1, 50) { LeaderId = 1 }],
            [new(1, "city", 1, 1, 1, 100, 100, 100, 50, [])
                { Commerce = 300, MaxCommerce = 500, Cultivation = 200, MaxCultivation = 500 }], [], [])
        {
            FreeOfficerMigrationEnabled = false,
            Officers = [new("ruler", new(1, 1, 1, new(60, 70, 80, 90), false)),
                new("free", new OfficerState(2, null, 1, new(50, 50, 50, 50), false)
                    { Status = OfficerStatus.Free, BirthYear = 170 }.WithLoyalty(1, 255))],
            Information = new([], [], [], [new(1, 1, 1, new(10000, 20000)) { IsRoot = true }], [])
        };
        File.WriteAllBytes(FilePath("scenario.json"), CanonicalJson.Encode(definition));
        var result = Run("verify", "--scenario", FilePath("scenario.json"), "--xuns", "3", "--output", _directory);
        Assert.Equal(0, result.Code);
        Assert.Equal(1, ReadSession("continuous.save.json").World.Officers.Single(o => o.Id == 2).OwnerId);
        Assert.Contains("respond-office-request", File.ReadAllText(FilePath("commands.jsonl")));
    }

    [Theory]
    [InlineData(190)]
    [InlineData(42)]
    public void AutomaticControlsWithoutProactiveAiRestoreAndReplay(int seed)
    {
        var verified = Run("verify", "--fixture", "--strategy", "autonomous", "--xuns", "6",
            "--seed", seed.ToString(), "--output", _directory);
        Assert.Equal(0, verified.Code);
        Assert.Equal("original-complete-v1", verified.Report.GetProperty("proactiveAi").GetString());
        Assert.Equal(6, verified.Report.GetProperty("completedXun").GetInt32());
        var initial = StrictJson.Deserialize<SaveEnvelope>(File.ReadAllBytes(FilePath("initial.save.json"))).Session;
        Assert.All(initial.World.Simulation.Controls, seat => Assert.Equal(ControllerKind.Automatic, seat.Kind));
        Assert.Equal(0, initial.World.Simulation.ComputerDamageBonusPercent);
        var logReplay = Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("initial.save.json"),
            "--report", FilePath("autonomous-log.json"), "--save", FilePath("autonomous-log.save.json"));
        Assert.Equal(0, logReplay.Code);
        StateAssert.Equal(ReadSession("continuous.save.json"), ReadSession("autonomous-log.save.json"));
        var freshReplay = Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--report", FilePath("autonomous-fresh.json"), "--save", FilePath("autonomous-fresh.save.json"));
        Assert.Equal(0, freshReplay.Code);
        StateAssert.Equal(ReadSession("continuous.save.json"), ReadSession("autonomous-fresh.save.json"));
    }

    [Fact]
    public void RemovedEvaluationBudgetIsAUsageFailure()
    {
        var result = Run("verify", "--fixture", "--strategy", "autonomous", "--ai-budget", "0", "--output", _directory);
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("replay", "--recording")]
    [InlineData("run", "--record")]
    public void RemovedRecordingModeAndOptionsAreUsageFailures(string mode, string option)
    {
        var result = Run(mode, "--fixture", option, FilePath("obsolete.json"), "--report", FilePath("obsolete-report.json"));
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
        Assert.False(File.Exists(FilePath("obsolete.json")));
    }

    [Fact]
    public void PlayerLoadExecutesRemainingScriptAndRejectsMixedRestoreModes()
    {
        Assert.Equal(0, Run("verify", "--fixture", "--xuns", "2", "--output", _directory).Code);
        var loaded = Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--load", FilePath("checkpoint.save.json"),
            "--report", FilePath("loaded-report.json"), "--save", FilePath("loaded.save.json"));
        Assert.Equal(0, loaded.Code);
        Assert.Equal(1, loaded.Report.GetProperty("completedXun").GetInt32());
        Assert.True(loaded.Report.GetProperty("alreadyProcessedOperations").GetInt32() > 0);
        Assert.DoesNotContain("operations", File.ReadAllText(FilePath("loaded.save.json")));
        StateAssert.NotEqual(ReadSession("continuous.save.json"), ReadSession("loaded.save.json"));
        var mixed = Run("run", "--fixture", "--log", FilePath("commands.jsonl"),
            "--load", FilePath("checkpoint.save.json"), "--restore", FilePath("checkpoint.save.json"),
            "--report", FilePath("mixed-report.json"));
        Assert.Equal(2, mixed.Code);
    }

    [Fact]
    public void MissingLoggedControllerCannotSilentlyUseTheDefault()
    {
        WriteFailureScenario();
        var lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var header = JsonNode.Parse(lines[0])!;
        header.AsObject().Remove("playerForceController");
        lines[0] = header.ToJsonString();
        File.WriteAllLines(FilePath("invalid-policy.jsonl"), lines);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("invalid-policy.jsonl"),
            "--report", FilePath("invalid-policy-report.json"));
        Assert.Equal(5, result.Code);
        Assert.Equal(1, result.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLoggedPolicyIsAReplayFailure(bool unknownRuler)
    {
        WriteFailureScenario();
        var lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var header = JsonNode.Parse(lines[0])!;
        if (unknownRuler) header["originalAi"]!["preferredStrategyRulerIds"] = new JsonArray(9001);
        else header["originalAi"]!["workRules"]!.AsObject().Remove("Trade");
        lines[0] = header.ToJsonString();
        File.WriteAllLines(FilePath("invalid-policy.jsonl"), lines, System.Text.Encoding.UTF8);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("invalid-policy.jsonl"),
            "--report", FilePath("invalid-policy-report.json"));
        Assert.Equal(5, result.Code);
        Assert.Equal("replay", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
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
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        SaveService.Write(FilePath("initial.save.json"), session);
        Assert.Equal(SessionDisposition.Accepted, ApplyInput(session.Request(new ChronicleOperation("accepted prefix"))).Disposition);
        CommandLogService.Write(FilePath("commands.jsonl"), inputHeader, requests);
    }

    [Fact]
    public void CommandLogCreatesReplayWithRecordedNewGameConfiguration()
    {
        var content = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "bonus-cli", 190, 1, 1, null,
            [new(1, "force", 0, 100) { LeaderId = 1 }],
            [new(101, "city", 1, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Officers = [new("ruler", new(1, 1, 101, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler })] , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }], []) };
        string scenario = FilePath("bonus-scenario.json"), log = FilePath("bonus.commands.jsonl");
        File.WriteAllBytes(scenario, CanonicalJson.Encode(content));
        var session = GameSession.Start(content, 190, 100, settings: new()
        {
            Difficulty = GameDifficulty.Beginner, Mode = GameMode.Fictional,
            MoneyBonus = 73, FoodBonus = 80, DomesticBonus = 9, SoldierBonus = 800, MoraleBonus = 4
        }, playerForceIds: [1]);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
AdvanceInput();
        CommandLogService.Write(log, inputHeader, requests);
        var result = Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("bonus-report.json"), "--save", FilePath("bonus-report.save.json"));
        Assert.Equal(0, result.Code);
        StateAssert.Equal(session.Capture(), ReadSession("bonus-report.save.json"));
    }
    [Fact]
    public void SearchDiscoveryRecordsRealAnswerAndResumesBeforeSaving()
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
        string scenario = FilePath("search-scenario.json"), log = FilePath("search.commands.jsonl");
        File.WriteAllBytes(scenario, San9.Core.Simulation.CanonicalJson.Encode(content));
        var session = San9.Application.GameSession.Start(content, 190);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        San9.Core.Simulation.CommandExecutionResult ExecuteInput(San9.Core.Simulation.CommandContext context, San9.Core.Simulation.GameCommand command)
        {
            var request = session.Request(new CommandOperation(command), context.ControllerId, command.CommandId);
            var result = session.Execute(context, command);
            if (result.Submission.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        Assert.True(ExecuteInput(new("player"), new San9.Core.Simulation.SearchCommand(Guid.NewGuid(), 1, 0, [2], 101)).Submission.Accepted);
AdvanceInput();
        var prompt = session.GetSearchRecruitmentPrompt()!;
        Assert.NotNull(prompt);
        Assert.True(ExecuteInput(new("player"), new San9.Core.Simulation.RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, false)).Submission.Accepted);
        San9.Application.Persistence.CommandLogService.Write(log, inputHeader, requests);
        var executed = Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("search-run.json"),
            "--save", FilePath("search.save.json"));
        Assert.Equal(0, executed.Code);
        StateAssert.Equal(session.Capture(), ReadSession("search.save.json"));
        var restored = San9.Application.Persistence.SaveService.Read(FilePath("search.save.json"), content);
        Assert.Equal(San9.Core.Domain.OfficerStatus.Free, restored.Snapshot.Officers.Single(o => o.Id == 3).Status);
        Assert.Equal(new San9.Core.Simulation.GameClock(190, 1, 11), restored.Snapshot.Clock);
    }

    [Theory]
    [InlineData("community")] [InlineData("conflict")] [InlineData("merchant")]
    public void TypedExplorationChoicesRunFromExactSaveAndReplay(string kind)
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
        string scenario = FilePath("g10.json"), log = FilePath("g10.commands.jsonl"), initial = FilePath("g10.initial.json");
        File.WriteAllBytes(scenario, JsonSerializer.SerializeToUtf8Bytes(d, San9.Content.Packages.ScenarioContentFiles.DataJsonOptions));
        var session = GameSession.Start(d, 190);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        San9.Core.Simulation.CommandExecutionResult ExecuteInput(San9.Core.Simulation.CommandContext context, San9.Core.Simulation.GameCommand command)
        {
            var request = session.Request(new CommandOperation(command), context.ControllerId, command.CommandId);
            var result = session.Execute(context, command);
            if (result.Submission.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        SaveService.Write(initial, session);         Assert.True(ExecuteInput(new("player"), new SearchCommand(Guid.NewGuid(), 1, 0, [2], 101)).Submission.Accepted);
AdvanceInput();
        var task = session.Snapshot.Simulation.Tasks.Single(t => t.Id == session.Snapshot.Simulation.PendingDailyTask!.RemainingTaskIds[0]);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, task.ResolutionStep, true);
        answer = kind switch
        {
            "community" => answer with { CommunitySearch = CommunitySearchChoice.Accept },
            "conflict" => answer with { SearchConflict = SearchConflictChoice.Pay },
            _ => answer with { MerchantInvestment = new(MerchantInvestmentChoice.Invest, 100) }
        };
        Assert.True(ExecuteInput(new("player"), answer).Submission.Accepted);
        CommandLogService.Write(log, inputHeader, requests);
        var executed = Run("run", "--scenario", scenario, "--restore", initial, "--log", log,
            "--report", FilePath("g10-run.json"), "--save", FilePath("g10-final.json"));
        Assert.True(executed.Code == 0, executed.Report.ToString()); StateAssert.Equal(session.Capture(), ReadSession("g10-final.json"));
    }

    [Fact]
    public void VictoryEndingCanBeSelectedSavedAndReplayedByHost()
    {
        var content = new San9.Content.Scenarios.ScenarioInitialState(San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion,
            "ending-cli", 220, 1, 1, 9, [new(9, "force", 0, 500) { LeaderId = 17 }],
            [new(101, "city", 9, 0, 0, 1000, 100, 100, 50, [])], [], [])
        {
            Officers = [new("ruler", new(17, 9, 101, new(80, 80, 80, 80), false))],
            Terminal = new([101], 350), Ending = new([101], [101], [101], [101], [101], [101], [])
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(9, 9, 17, new(1000, 1000)) { IsRoot = true }], []) };
        string scenario = FilePath("ending-scenario.json"), log = FilePath("ending.commands.jsonl");
        File.WriteAllBytes(scenario, San9.Core.Simulation.CanonicalJson.Encode(content));
        var session = San9.Application.GameSession.Start(content, 1);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        var request = session.Request(new San9.Application.EndingOperation(new(San9.Core.Domain.EndingPolicy.DomesticProsperity, null)));
        Assert.Equal(San9.Application.SessionDisposition.Accepted, ApplyInput(request).Disposition);
        San9.Application.Persistence.CommandLogService.Write(log, inputHeader, requests);
        var executed = Run("run", "--scenario", scenario, "--log", log, "--report", FilePath("ending-run.json"),
            "--save", FilePath("ending.save.json"));
        Assert.Equal(0, executed.Code);
        StateAssert.Equal(session.Capture(), ReadSession("ending.save.json"));
        Assert.Equal((int)San9.Core.Domain.EndingOutcome.FallenState, executed.Report.GetProperty("ending").GetProperty("outcome").GetInt32());
        Assert.Equal(session.Ending, San9.Application.Persistence.SaveService.Read(FilePath("ending.save.json"), content).Ending);
    }

    [Fact]
    public void AutomaticDiplomacyRecordsAnswersAndSavesAfterResolution()
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
        var session = GameSession.Start(definition, 190);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        San9.Core.Simulation.CommandExecutionResult ExecuteInput(San9.Core.Simulation.CommandContext context, San9.Core.Simulation.GameCommand command)
        {
            var request = session.Request(new CommandOperation(command), context.ControllerId, command.CommandId);
            var result = session.Execute(context, command);
            if (result.Submission.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        SessionResult EndInput(San9.Core.Simulation.CommandContext context) => ApplyInput(session.Request(new EndControlOperation(), context.ControllerId));
        SaveService.Write(FilePath("initial.json"), session);
EndInput(new("player"));
        Assert.True(ExecuteInput(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 4, 1,
            new(GiftKind.Money, 2000, null))).Submission.Accepted);
AdvanceInput();
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        Assert.True(ExecuteInput(new("player"), new RespondDailyTaskCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, pending.RemainingTaskIds[0], 0, false)).Submission.Accepted);
        CommandLogService.Write(FilePath("commands.jsonl"), inputHeader, requests);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--restore", FilePath("initial.json"),
            "--log", FilePath("commands.jsonl"), "--save", FilePath("resolved.json"), "--report", FilePath("report.json"));
        Assert.Equal(0, result.Code);
        Assert.True(result.Report.GetProperty("diplomacyStrategyEvents").GetProperty("gift-resolved").GetInt32() > 0);
        Assert.Contains("respond-daily-task", File.ReadAllText(FilePath("commands.jsonl")));
        StateAssert.Equal(session.Capture(), SaveService.Read(FilePath("resolved.json"), definition).Capture());
    }

    [Fact]
    public void MilitaryScriptReportsDeploymentMovementAndSingleAbsorption()
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
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        San9.Core.Simulation.CommandExecutionResult ExecuteInput(San9.Core.Simulation.CommandContext context, San9.Core.Simulation.GameCommand command)
        {
            var request = session.Request(new CommandOperation(command), context.ControllerId, command.CommandId);
            var result = session.Execute(context, command);
            if (result.Submission.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        Assert.True(ExecuteInput(new("player"), new San9.Core.Simulation.TransportUnitCommand(Guid.NewGuid(), 1, 0,
            1, [1], 400, 2, 20, 30)).Submission.Accepted);
AdvanceInput();
        File.WriteAllBytes(FilePath("scenario.json"), San9.Core.Simulation.CanonicalJson.Encode(definition));
        CommandLogService.Write(FilePath("military.commands.jsonl"), inputHeader, requests);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("military.commands.jsonl"),
            "--report", FilePath("military-report.json"), "--save", FilePath("military-report.save.json"));
        Assert.Equal(0, result.Code);
        Assert.Equal("G05", result.Report.GetProperty("goal").GetString());
        Assert.Equal(1, result.Report.GetProperty("militaryEvents").GetProperty("unit-deployed").GetInt32());
        Assert.Equal(1, result.Report.GetProperty("militaryEvents").GetProperty("unit-absorbed").GetInt32());
        Assert.True(result.Report.GetProperty("militaryEvents").GetProperty("unit-moved").GetInt32() > 0);
        StateAssert.Equal(session.Capture(), ReadSession("military-report.save.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EconomicYearUsesAllProjectsAndSurvivesDiskRestore(bool policy)
    {
        string[] args = policy ? ["verify", "--fixture", "--strategy", "simple", "--xuns", "36", "--output", _directory] :
            ["verify", "--fixture", "--xuns", "36", "--output", _directory];
        var result = Run(args);
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
    public void PersonnelCommandsAndReplayReportTheirActualScope()
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
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        San9.Core.Simulation.CommandExecutionResult ExecuteInput(San9.Core.Simulation.CommandContext context, San9.Core.Simulation.GameCommand command)
        {
            var request = session.Request(new CommandOperation(command), context.ControllerId, command.CommandId);
            var result = session.Execute(context, command);
            if (result.Submission.Accepted)
                requests.Add(request);
            return result;
        }
        Assert.True(ExecuteInput(new("player"), new RewardOfficerCommand(Guid.NewGuid(), 1, 0, 2)).Submission.Accepted);
        Assert.True(ExecuteInput(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 1, 3)).Submission.Accepted);
        CommandLogService.Write(FilePath("personnel.jsonl"), inputHeader, requests);
        var run = Run("run", "--scenario", scenario, "--log", FilePath("personnel.jsonl"), "--report", FilePath("personnel-report.json"), "--save", FilePath("personnel-report.save.json"));
        Assert.Equal(0, run.Code);
        StateAssert.Equal(session.Capture(), ReadSession("personnel-report.save.json"));
        Assert.Equal("G04", run.Report.GetProperty("goal").GetString());
        Assert.Equal(1, run.Report.GetProperty("personnelEvents").GetProperty("officer-rewarded").GetInt32());
        Assert.Equal(1, run.Report.GetProperty("personnelEvents").GetProperty("adviser-appointed").GetInt32());
    }

    [Fact]
    public void HostRunsCommandsSavesResumesAndSkipsRepeatedPrefix()
    {
        var verification = Run("verify", "--fixture", "--xuns", "2", "--output", _directory);
        Assert.Equal(0, verification.Code);
        Assert.True(verification.Report.GetProperty("success").GetBoolean());
        Assert.Equal(2, verification.Report.GetProperty("completedXun").GetInt32());
        // Explicit scripts and the production original economic cycle share public commands.
        Assert.True(verification.Report.GetProperty("developmentCommands").GetInt32() > 2);
        var readOptions = new JsonSerializerOptions(CanonicalJson.Options) { AllowOutOfOrderMetadataProperties = true };
        var requests = File.ReadLines(FilePath("commands.jsonl")).Skip(1)
            .Select(line => JsonSerializer.Deserialize<SessionRequest>(line, readOptions)!).ToArray();
        Assert.Contains(requests, request => request.ControllerId.StartsWith("ai:", StringComparison.Ordinal) && request.Operation is EndControlOperation);
        Assert.DoesNotContain(requests, request => request.ControllerId.StartsWith("ai:", StringComparison.Ordinal) && request.Operation is CommandOperation);
        var finalState = ReadSession("continuous.save.json");
        var replay = Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("initial.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("replay-report.json"));
        Assert.Equal(0, replay.Code);
        StateAssert.Equal(ReadSession("continuous.save.json"), ReadSession("replay.save.json"));
        Assert.Equal(File.ReadAllBytes(FilePath("events.jsonl")), File.ReadAllBytes(FilePath("replay-report.events.jsonl")));
        var repeated = Run("run", "--fixture", "--log", FilePath("commands.jsonl"), "--restore", FilePath("replay.save.json"),
            "--save", FilePath("replay.save.json"), "--report", FilePath("duplicate-report.json"));
        Assert.Equal(0, repeated.Code);
        Assert.Equal(0, repeated.Report.GetProperty("acceptedOperations").GetInt32());
        Assert.Equal(requests.Length, repeated.Report.GetProperty("alreadyProcessedOperations").GetInt32());
        Assert.Equal(0, new FileInfo(FilePath("duplicate-report.events.jsonl")).Length);
    }

    [Fact]
    public void MalformedLogReportsFailureLineAndKeepsAcceptedProgress()
    {
        WriteFailureScenario();
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        File.WriteAllLines(FilePath("bad.jsonl"), [lines[0], lines[1], "{broken}"]);
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("bad.jsonl"), "--save", FilePath("partial.save.json"),
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
    public void OlderRulesWarnInReportAndStandardErrorWhileContinuing()
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
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("older.jsonl"),
            "--restore", FilePath("older.save.json"), "--report", FilePath("warnings.json"));
        Assert.Equal(0, result.Code);
        Assert.True(result.Report.GetProperty("success").GetBoolean());
        Assert.Equal(1, result.Report.GetProperty("acceptedOperations").GetInt32());
        Assert.Equal(new[] { "older-save-rules", "older-log-rules" }, result.Report.GetProperty("rulesVersionWarnings")
            .EnumerateArray().Select(w => w.GetProperty("savedVersion").GetString()));
    }

    [Fact]
    public void UnknownSaveVersionHasSpecificFailureCode()
    {
        WriteFailureScenario();
        var save = JsonNode.Parse(File.ReadAllBytes(FilePath("initial.save.json")))!;
        save["formatVersion"] = 999;
        File.WriteAllText(FilePath("old.save.json"), save.ToJsonString());
        var result = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"), "--restore", FilePath("old.save.json"),
            "--report", FilePath("old-report.json"));
        Assert.Equal(4, result.Code);
        Assert.Equal("save-UnsupportedVersion", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Fact]
    public void UnknownCommandAndConflictingPathsAreRejected()
    {
        WriteFailureScenario();
        string[] lines = File.ReadAllLines(FilePath("commands.jsonl"));
        var request = JsonNode.Parse(lines[1])!;
        request["operation"]!["type"] = "teleport";
        File.WriteAllLines(FilePath("unknown.jsonl"), [lines[0], request.ToJsonString()]);
        var unknown = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("unknown.jsonl"), "--report", FilePath("unknown-report.json"));
        Assert.Equal(5, unknown.Code);
        Assert.Equal(2, unknown.Report.GetProperty("firstFailure").GetProperty("line").GetInt32());
        byte[] before = File.ReadAllBytes(FilePath("commands.jsonl"));
        var conflict = Run("run", "--scenario", FilePath("scenario.json"), "--log", FilePath("commands.jsonl"), "--report", FilePath("commands.jsonl"));
        Assert.Equal(2, conflict.Code);
        Assert.Equal(before, File.ReadAllBytes(FilePath("commands.jsonl")));
    }

    [Fact]
    public void UsageFailureStillWritesMachineReadableStandardOutput()
    {
        var result = Run("unsupported");
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("report.json")]
    [InlineData("divergence.json")]
    [InlineData("initial.save.json")]
    [InlineData("checkpoint.save.json")]
    [InlineData("continuous.save.json")]
    [InlineData("restored.save.json")]
    [InlineData("commands.jsonl")]
    [InlineData("events.jsonl")]
    [InlineData("checkpoints.json")]

    public void VerificationRejectsOutputCollisionsBeforeReadingOrOverwritingTheScenario(string name)
    {
        byte[] original = "scenario input must remain intact"u8.ToArray();
        File.WriteAllBytes(FilePath(name), original);
        var result = Run("verify", "--scenario", FilePath(name), "--output", Path.Combine(_directory, "."));
        Assert.Equal(2, result.Code);
        Assert.Equal("usage", result.Report.GetProperty("firstFailure").GetProperty("code").GetString());
        Assert.Equal(original, File.ReadAllBytes(FilePath(name)));
        Assert.Single(Directory.GetFiles(_directory));
    }

    private static (int Code, JsonElement Report) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = HeadlessHost.Run(args, output, error);
        using var document = JsonDocument.Parse(output.ToString());
        string expectedErrors = string.Concat(document.RootElement.GetProperty("rulesVersionWarnings")
            .EnumerateArray().Select(w => w.GetProperty("message").GetString() + Environment.NewLine));
        Assert.Equal(expectedErrors, error.ToString());
        return (code, document.RootElement.Clone());
    }
}
