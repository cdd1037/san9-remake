using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AutomaticResponseFailure(string Code, string Kind, string Identity, string ControllerId,
    CommandRejection Rejection = CommandRejection.None);

internal sealed class AutomaticResponseException(AutomaticResponseFailure failure) : Exception(failure.Code)
{
    internal AutomaticResponseFailure Failure { get; } = failure;
}

/// <summary>Mandatory responses have their own bounded evaluation allowance, independent of turn work.</summary>
internal static class AutomaticResponseRunner
{
    internal static void Run(SimulationEngine engine, List<DomainEvent> events, List<WorldSnapshot>? frames = null)
    {
        HashSet<string> answered = [];
        while (engine.GetPendingResponse() is { Owner.Kind: ControllerKind.Automatic } response)
        {
            if (!answered.Add(response.Identity)) Fail(response, "response-no-progress");
            CommandExecutionResult result;
            try
            {
                var command = Choose(engine, response);
                result = engine.Execute(new(response.Owner.ControllerId), command, frames is not null);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                throw new AutomaticResponseException(new("response-rule-fault", response.Kind, response.Identity,
                    response.Owner.ControllerId));
            }
            if (!result.Submission.Accepted) Fail(response, "response-rejected", result.Submission.Rejection);
            events.AddRange(result.Events);
            if (frames is not null)
                foreach (var frame in result.PresentationFrames)
                {
                    if (frames.Count > 0 && frames[^1].Clock == frame.Clock) frames[^1] = frame;
                    else frames.Add(frame);
                }
        }
    }

    internal static GameCommand Choose(SimulationEngine engine, ResponseRequest response, int evaluationBudget = 2)
    {
        var snapshot = engine.Snapshot;
        var id = new Guid(Convert.FromHexString(CanonicalJson.Hash(new { snapshot.Simulation.RunSeed,
            response.Identity, response.Owner.ControllerId, snapshot.Simulation.NextSequence })).AsSpan(0, 16));
        if (response.Kind == "captured-ruler" && engine.GetCapturedRulerPrompt() is { } captive)
        {
            // Release is the explicit low-budget default. Optional comparison samples only the analysis stream.
            var action = CapturedRulerAction.Release;
            if (evaluationBudget >= 2)
            {
                var random = AiPlanningContext.Create(AiObservation.FromSnapshot(response.Owner.ForceId, snapshot),
                    $"response:{response.Kind}:{captive.RulerId}", new(2, 0)).CreateAnalysisRandom();
                if (captive.IsAllianceTarget || random.NextUInt64() % 100 < (ulong)ExecutionChance(captive))
                    action = CapturedRulerAction.Execute;
            }
            if (!captive.LegalActions.Contains(action)) Fail(response, "response-no-legal-option");
            return new ChooseCapturedRulerDispositionCommand(id, response.Owner.ForceId,
                snapshot.Simulation.NextSequence, captive.RulerId, action);
        }
        if (response.Kind == "execution-succession" && engine.GetExecutionSuccessionPrompt() is { } succession)
        {
            if (succession.CandidateIds.IsEmpty) Fail(response, "response-no-legal-option");
            // The rule-defined automatic succession order is an explicit legal option, even with zero search.
            return new ChooseExecutionSuccessorCommand(id, response.Owner.ForceId,
                snapshot.Simulation.NextSequence, succession.RulerId, null);
        }
        Fail(response, "response-unsupported");
        throw new InvalidOperationException("Unreachable response dispatch.");
    }

    internal static int ExecutionChance(CapturedRulerPrompt captive)
    {
        var a = captive.RulerAbilities;
        int best = Math.Max(Math.Max(a.War, a.Intelligence), Math.Max(a.Politics, a.Leadership));
        return Math.Max(0, 70 - captive.CaptorBaseTrust / 10 - best / 2 - captive.RelationValue);
    }

    private static void Fail(ResponseRequest response, string code, CommandRejection rejection = CommandRejection.None) =>
        throw new AutomaticResponseException(new(code, response.Kind, response.Identity, response.Owner.ControllerId, rejection));
}
