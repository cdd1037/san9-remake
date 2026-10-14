namespace San9.Headless;

public sealed record HeadlessFailure(string Code, string Message, int? Line = null);
public sealed record ContentCoverage(int Forces, int Settlements, int Officers, int Units, int Corps, int Treasures, int Relations);
public sealed record AiTurnReport(int ForceId, Core.Simulation.GameClock Clock, long Work, bool SearchComplete,
    bool BudgetExhausted, long ElapsedMilliseconds, int AcceptedCommands, int RejectedCommands, int TargetSwitches, Dictionary<string, long> WorkByKind)
{
    public bool NoCommand => AcceptedCommands == 0;
}
public sealed record AiForceState(int ForceId, int Settlements, long Soldiers, long Money, long Food, int Campaigns, long CampaignNoProgressXun);
public sealed record AiStateCheckpoint(Core.Simulation.GameClock Clock, AiForceState[] Forces);
public sealed record EvaluationMachine(string Framework, string OperatingSystem, string Architecture, int LogicalProcessors);

public sealed class HeadlessReport
{
    public string Goal { get; set; } = "G03";
    public string Policy { get; set; } = "replay";
    public Dictionary<string, int> Activities { get; set; } = [];
    public long ResourceShortfall { get; set; }
    public long SaturationLoss { get; set; }
    public string Mode { get; set; } = "unknown";
    public bool Success { get; set; }
    public string RulesVersion { get; init; } = Application.SessionProtocol.RulesVersion;
    public List<Application.RulesVersionWarning> RulesVersionWarnings { get; set; } = [];
    public ulong? Seed { get; set; }
    public string? CommandLog { get; set; }
    public string? EventLog { get; set; }
    public string? AttemptLog { get; set; }
    public long CompletedXun { get; set; }
    public string TerminationReason { get; set; } = "failure";
    public Core.Simulation.TerminalResult? Terminal { get; set; }
    public Core.Domain.EndingResult? Ending { get; set; }
    public HeadlessFailure? FirstFailure { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int AcceptedOperations { get; set; }
    public int AlreadyProcessedOperations { get; set; }
    public long? FailureSequence { get; set; }
    public int DevelopmentCommands { get; set; }
    public int WaitCommands { get; set; }
    public Dictionary<string, int> PersonnelEvents { get; set; } = [];
    public Dictionary<string, int> LifecycleEvents { get; set; } = [];
    public Dictionary<string, int> MilitaryEvents { get; set; } = [];
    public Dictionary<string, int> DiplomacyStrategyEvents { get; set; } = [];
    public int RestoreCount { get; set; }
    public ContentCoverage? Coverage { get; set; }
    public Application.AI.AiPolicyConfiguration? AiConfiguration { get; set; }
    public EvaluationMachine Machine { get; } = new(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount);
    public Dictionary<int, Application.AI.AllocationWorkSummary> AiWorkByForce { get; set; } = [];
    public Dictionary<string, long> AiAcceptedCommands { get; set; } = [];
    public Dictionary<string, long> AiRejectedCommands { get; set; } = [];
    public Dictionary<Core.Simulation.CommandRejection, long> AiRejectionReasons { get; set; } = [];
    public long BattleSoldierLoss { get; set; }
    public long BattleDead { get; set; }
    public long UnrecoveredSoldiers { get; set; }
    public List<AiTurnReport> AiTurns { get; set; } = [];
    public List<AiStateCheckpoint> AiCheckpoints { get; set; } = [];
    public string Scope { get; set; } = "Domestic commands and economic cycles with scripted/simple policy, save and replay; no warfare or complete strategic AI.";
}
