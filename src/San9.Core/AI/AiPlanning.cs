using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.AI;

public sealed record AiObservation
{
    private AiObservation(int observerId, GameClock clock, string stateHash)
    {
        ObserverId = observerId;
        Clock = clock;
        StateHash = stateHash;
    }

    public int ObserverId { get; }

    public GameClock Clock { get; }

    public string StateHash { get; }

    public static AiObservation FromSnapshot(int observerId, WorldSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observerId);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.StateHash);
        return new AiObservation(observerId, snapshot.Clock, snapshot.StateHash);
    }
}

public sealed record AiPlanningBudget
{
    public AiPlanningBudget(long maxNodeEvaluations, int maxRollouts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodeEvaluations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRollouts);

        if (maxNodeEvaluations == 0 && maxRollouts == 0)
        {
            throw new ArgumentException("At least one deterministic planning budget must be positive.");
        }

        MaxNodeEvaluations = maxNodeEvaluations;
        MaxRollouts = maxRollouts;
    }

    public long MaxNodeEvaluations { get; }

    public int MaxRollouts { get; }
}

public enum AiDiagnosticLevel
{
    None = 0,
    Summary,
    Detailed,
}

public sealed record AiPlanningContext
{
    private AiPlanningContext(
        AiObservation observation,
        string purpose,
        AiPlanningBudget budget,
        AiDiagnosticLevel diagnosticLevel,
        ulong analysisSeed)
    {
        Observation = observation;
        Purpose = purpose;
        Budget = budget;
        DiagnosticLevel = diagnosticLevel;
        AnalysisSeed = analysisSeed;
    }

    public AiObservation Observation { get; }

    public string Purpose { get; }

    public AiPlanningBudget Budget { get; }

    public AiDiagnosticLevel DiagnosticLevel { get; }

    public ulong AnalysisSeed { get; }

    public StableRandom CreateAnalysisRandom() => new(AnalysisSeed);

    public static AiPlanningContext Create(
        AiObservation observation,
        string purpose,
        AiPlanningBudget budget,
        AiDiagnosticLevel diagnosticLevel = AiDiagnosticLevel.None)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(budget);

        if (!Enum.IsDefined(diagnosticLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(diagnosticLevel));
        }

        string canonical = string.Join(
            '|',
            observation.StateHash,
            observation.ObserverId,
            observation.Clock.Year,
            observation.Clock.Month,
            (int)observation.Clock.Xun,
            purpose);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        ulong analysisSeed = BinaryPrimitives.ReadUInt64BigEndian(digest);

        return new AiPlanningContext(observation, purpose, budget, diagnosticLevel, analysisSeed);
    }
}

public sealed record AiCommandProposal
{
    public AiCommandProposal(GameCommand command, long score, string rationale)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Command = command;
        Score = score;
        Rationale = rationale;
    }

    public GameCommand Command { get; }

    public long Score { get; }

    public string Rationale { get; }
}

public sealed record AiDiagnostic
{
    public AiDiagnostic(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}

public sealed record AiPlan
{
    public AiPlan(
        IEnumerable<AiCommandProposal> proposals,
        IEnumerable<AiDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        AiCommandProposal[] proposalArray = proposals.ToArray();
        if (proposalArray.Any(static proposal => proposal is null))
        {
            throw new ArgumentException("Proposals cannot contain null values.", nameof(proposals));
        }

        AiDiagnostic[] diagnosticArray = diagnostics?.ToArray() ?? [];
        if (diagnosticArray.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics cannot contain null values.", nameof(diagnostics));
        }

        Proposals = Array.AsReadOnly(proposalArray);
        Diagnostics = Array.AsReadOnly(diagnosticArray);
    }

    public IReadOnlyList<AiCommandProposal> Proposals { get; }

    public IReadOnlyList<AiDiagnostic> Diagnostics { get; }
}

public interface IAiPlanner
{
    AiPlan Plan(AiPlanningContext context);
}
