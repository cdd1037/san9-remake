namespace San9.Headless;

public sealed record HeadlessFailure(string Code, string Message, int? Line = null);
public sealed record ContentCoverage(int Forces, int Settlements, int Officers, int Units, int Corps, int Treasures, int Relations);

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
    public int HashVersion { get; init; } = Core.Simulation.CanonicalJson.Version;
    public string? ContentFingerprint { get; set; }
    public ulong? Seed { get; set; }
    public string? CommandLog { get; set; }
    public string? EventLog { get; set; }
    public string? AttemptLog { get; set; }
    public long CompletedXun { get; set; }
    public string TerminationReason { get; set; } = "failure";
    public string? FinalHash { get; set; }
    public string? WorldHash { get; set; }
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
    public string Scope { get; set; } = "Domestic commands and economic cycles with scripted/simple policy, save and replay; no warfare or complete strategic AI.";
}
