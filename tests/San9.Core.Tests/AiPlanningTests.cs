using San9.Core.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

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

        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
        Assert.Equal(expected.Events, actual.Events);
    }

    [Fact]
    public void Planner_IsDeterministicForSameObservationAndBudget()
    {
        SimulationEngine engine = CreateEngine();
        IAiPlanner planner = new SamplePlanner();
        AiPlanningContext first = CreateContext(engine, AiDiagnosticLevel.None);
        AiPlanningContext second = CreateContext(engine, AiDiagnosticLevel.None);

        AiPlan firstPlan = planner.Plan(first);
        AiPlan secondPlan = planner.Plan(second);

        Assert.Equal(first.AnalysisSeed, second.AnalysisSeed);
        Assert.Equal(firstPlan.Proposals, secondPlan.Proposals);
        Assert.Equal(firstPlan.Diagnostics, secondPlan.Diagnostics);
    }

    [Fact]
    public void DiagnosticLevel_DoesNotChangeProposals()
    {
        SimulationEngine engine = CreateEngine();
        IAiPlanner planner = new SamplePlanner();

        AiPlan withoutDiagnostics = planner.Plan(CreateContext(engine, AiDiagnosticLevel.None));
        AiPlan withDiagnostics = planner.Plan(CreateContext(engine, AiDiagnosticLevel.Detailed));

        Assert.Equal(withoutDiagnostics.Proposals, withDiagnostics.Proposals);
        Assert.Empty(withoutDiagnostics.Diagnostics);
        Assert.NotEmpty(withDiagnostics.Diagnostics);
    }

    [Fact]
    public void CandidateCommand_UsesSharedValidationAndRejectedCandidateDoesNotCommit()
    {
        SimulationEngine engine = CreateEngine();
        WaitCommand invalid = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), new ForceId(1), -1);
        AiPlan plan = new([new AiCommandProposal(invalid, 100, "invalid test candidate")]);

        CommandSubmissionResult validation = engine.Validate(plan.Proposals[0].Command);
        CommandSubmissionResult submission = engine.Submit(plan.Proposals[0].Command);
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
        AiObservation observation = AiObservation.FromSnapshot(new ForceId(1), engine.Snapshot);
        AiPlanningBudget budget = new(1, 0);

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
        AiObservation observation = AiObservation.FromSnapshot(new ForceId(1), engine.Snapshot);
        AiPlanningBudget budget = new(1, 0);

        AiPlanningContext strategic = AiPlanningContext.Create(observation, "strategic-turn", budget);
        AiPlanningContext tactical = AiPlanningContext.Create(observation, "tactical-turn", budget);

        Assert.NotEqual(strategic.AnalysisSeed, tactical.AnalysisSeed);
    }

    private static AiPlanningContext CreateContext(
        SimulationEngine engine,
        AiDiagnosticLevel diagnosticLevel)
    {
        AiObservation observation = AiObservation.FromSnapshot(new ForceId(1), engine.Snapshot);
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

    private sealed class SamplePlanner : IAiPlanner
    {
        public AiPlan Plan(AiPlanningContext context)
        {
            StableRandom random = context.CreateAnalysisRandom();
            List<AiCommandProposal> proposals = [];
            int count = Math.Min(context.Budget.MaxRollouts, 2);
            for (int index = 0; index < count; index++)
            {
                ulong roll = random.NextUInt64();
                WaitCommand command = new(
                    CreateGuid(roll, index),
                    context.Observation.ObserverId,
                    index);
                proposals.Add(new AiCommandProposal(command, unchecked((long)roll), $"candidate-{index}"));
            }

            IEnumerable<AiDiagnostic> diagnostics = context.DiagnosticLevel == AiDiagnosticLevel.None
                ? []
                : [new AiDiagnostic("AI-PLAN", $"Generated {proposals.Count} candidates.")];
            return new AiPlan(proposals, diagnostics);
        }

        private static Guid CreateGuid(ulong roll, int index)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(roll).CopyTo(bytes, 0);
            BitConverter.GetBytes(index).CopyTo(bytes, 8);
            return new Guid(bytes);
        }
    }
}
