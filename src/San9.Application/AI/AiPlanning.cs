using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AiObservation
{
    private AiObservation(int observerId, GameClock clock, ulong analysisSeed)
    {
        ObserverId = observerId;
        Clock = clock;
        AnalysisSeed = analysisSeed;
    }

    public int ObserverId { get; }

    public GameClock Clock { get; }

    public ulong AnalysisSeed { get; }

    public static AiObservation FromSnapshot(int observerId, WorldSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observerId);
        ArgumentNullException.ThrowIfNull(snapshot);
        ulong seed = StableRandom.DeriveSeed(snapshot.Simulation.RunSeed,
            FormattableString.Invariant($"ai:{observerId}:{snapshot.Clock.DayNumber}"));
        return new AiObservation(observerId, snapshot.Clock, seed);
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

        ulong analysisSeed = StableRandom.DeriveSeed(observation.AnalysisSeed, purpose);

        return new AiPlanningContext(observation, purpose, budget, diagnosticLevel, analysisSeed);
    }
}
