namespace San9.Headless;

public sealed record HeadlessFailure(string Code, string Message, int? Line = null);
public sealed record ContentCoverage(int Forces, int Settlements, int Officers, int Units, int Corps, int Treasures, int Relations);

public sealed class HeadlessReport
{
    public string Goal { get; init; } = "G02";
    public string Mode { get; set; } = "unknown";
    public bool Success { get; set; }
    public string RulesVersion { get; init; } = Application.SessionProtocol.RulesVersion;
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
    public HeadlessFailure? FirstFailure { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int AcceptedOperations { get; set; }
    public int AlreadyProcessedOperations { get; set; }
    public long? FailureSequence { get; set; }
    public int DevelopmentCommands { get; set; }
    public int WaitCommands { get; set; }
    public int RestoreCount { get; set; }
    public ContentCoverage? Coverage { get; set; }
    public string Scope { get; init; } = "Scripted timing, persistence and replay; no economy cycle, autonomous AI or warfare.";
}
