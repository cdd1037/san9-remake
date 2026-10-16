using San9.Tests;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AiPlanningTests
{
    [Fact]
    public void AnalysisRandom_DoesNotConsumeAuthoritativeRandomStream()
    {
        SimulationEngine withAnalysis = CreateEngine();
        SimulationEngine withoutAnalysis = CreateEngine();
        AiPlanningContext context = CreateContext(withAnalysis, AiDiagnosticLevel.Detailed);

        StableRandom analysisRandom = context.CreateAnalysisRandom();
        for (int index = 0; index < 100; index++)
        {
            _ = analysisRandom.NextUInt64();
        }

        AdvanceXunResult expected = withoutAnalysis.AdvanceXun();
        AdvanceXunResult actual = withAnalysis.AdvanceXun();

        StateAssert.Equal(expected.Snapshot, actual.Snapshot);
        Assert.Equal(expected.Events, actual.Events);
    }

    [Fact]
    public void AnalysisRandom_IsDeterministicForSameObservationAndPurpose()
    {
        SimulationEngine engine = CreateEngine();
        AiPlanningContext first = CreateContext(engine, AiDiagnosticLevel.None);
        AiPlanningContext second = CreateContext(engine, AiDiagnosticLevel.None);

        Assert.Equal(first.AnalysisSeed, second.AnalysisSeed);
        var a = first.CreateAnalysisRandom();
        var b = second.CreateAnalysisRandom();
        for (int index = 0; index < 100; index++) Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void CandidateCommand_UsesSharedValidationAndRejectedCandidateDoesNotCommit()
    {
        SimulationEngine engine = CreateEngine();
        WaitCommand invalid = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), 1, -1);
        CommandSubmissionResult validation = engine.Validate(new("ai"), invalid);
        CommandSubmissionResult submission = engine.Execute(new("ai"), invalid).Submission;
        AdvanceXunResult result = engine.AdvanceXun();

        Assert.Equal(CommandRejection.NegativeSequence, validation.Rejection);
        Assert.Equal(validation, submission);
        Assert.Empty(result.Events.OfType<CommandResolvedEvent>());
    }

    [Fact]
    public void PlanningBudget_RejectsWallClockFreeZeroAndNegativeLimits()
    {
        Assert.Throws<ArgumentException>(() => new AiPlanningBudget(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiPlanningBudget(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiPlanningBudget(0, -1));
    }

    [Fact]
    public void PlanningContext_RejectsInvalidPurposeAndDiagnosticLevel()
    {
        SimulationEngine engine = CreateEngine();
        AiObservation observation = AiObservation.FromSnapshot(1, engine.Snapshot);
        AiPlanningBudget budget = new(1, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => AiObservation.FromSnapshot(-1, engine.Snapshot));
        Assert.Throws<ArgumentException>(() => AiPlanningContext.Create(observation, "", budget));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiPlanningContext.Create(
            observation,
            "strategic-turn",
            budget,
            (AiDiagnosticLevel)999));
    }

    [Fact]
    public void PlanningPurpose_DerivesAnIndependentAnalysisSeed()
    {
        SimulationEngine engine = CreateEngine();
        AiObservation observation = AiObservation.FromSnapshot(1, engine.Snapshot);
        AiPlanningBudget budget = new(1, 0);

        AiPlanningContext strategic = AiPlanningContext.Create(observation, "strategic-turn", budget);
        AiPlanningContext tactical = AiPlanningContext.Create(observation, "tactical-turn", budget);

        Assert.NotEqual(strategic.AnalysisSeed, tactical.AnalysisSeed);
    }

    private static AiPlanningContext CreateContext(
        SimulationEngine engine,
        AiDiagnosticLevel diagnosticLevel)
    {
        AiObservation observation = AiObservation.FromSnapshot(1, engine.Snapshot);
        return AiPlanningContext.Create(
            observation,
            "strategic-turn",
            new AiPlanningBudget(maxNodeEvaluations: 16, maxRollouts: 4),
            diagnosticLevel);
    }

    private static SimulationEngine CreateEngine()
    {
        WorldState world = WorldState.Create(
            new GameClock(190, 1, XunIndex.Early),
            seed: 190UL);
        return new SimulationEngine(world);
    }
}
