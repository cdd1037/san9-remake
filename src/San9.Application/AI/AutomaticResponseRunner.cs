using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AutomaticResponseFailure(string Code, string Kind, string ControllerId,
    CommandRejection Rejection = CommandRejection.None);

internal sealed class AutomaticResponseException(AutomaticResponseFailure failure) : Exception(failure.Code)
{
    internal AutomaticResponseFailure Failure { get; } = failure;
}

/// <summary>Mandatory choices consume pending decisions independently of turn work.</summary>
internal static class AutomaticResponseRunner
{
    internal static void Run(SimulationEngine engine, List<DomainEvent> events, List<WorldSnapshot>? frames = null)
    {
        while (engine.GetPendingResponse() is { Owner.Kind: ControllerKind.Automatic } response)
        {
            CommandExecutionResult result;
            try
            {
                var command = Choose(engine, response);
                result = engine.Execute(new(response.Owner.ControllerId), command, frames is not null);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                throw new AutomaticResponseException(new("response-rule-fault", response.Kind,
                    response.Owner.ControllerId));
            }
            if (!result.Submission.Accepted) Fail(response, "response-rejected", result.Submission.Rejection);
            if (engine.GetPendingResponse() == response) Fail(response, "response-no-progress");
            events.AddRange(result.Events);
            if (frames is not null)
                foreach (var frame in result.PresentationFrames)
                {
                    if (frames.Count > 0 && frames[^1].Clock == frame.Clock) frames[^1] = frame;
                    else frames.Add(frame);
                }
        }
    }

    internal static GameCommand Choose(SimulationEngine engine, ResponseRequest response)
    {
        var snapshot = engine.Snapshot;
        var id = SimulationIdentity.Command(snapshot.Simulation.NextSequence, response.Owner.ForceId);
        if (response.Kind == "captured-ruler" && engine.GetCapturedRulerPrompt() is { } captive)
        {
            var random = AiPlanningContext.Create(AiObservation.FromSnapshot(response.Owner.ForceId, snapshot),
                $"response:{response.Kind}:{captive.RulerId}", new(1, 0)).CreateAnalysisRandom();
            var action = captive.IsAllianceTarget || random.NextUInt64() % 100 < (ulong)ExecutionChance(captive)
                ? CapturedRulerAction.Execute : CapturedRulerAction.Release;
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
        if (response.Kind == "sniping-succession" && engine.GetSnipingSuccessionPrompt() is { } sniping)
            return new ChooseSnipingSuccessorCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, sniping.RulerId, null);
        if (response.Kind == "duel-succession" && engine.GetDuelSuccessionPrompt() is { } duel)
            return new ChooseDuelSuccessorCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, duel.RulerId, null);
        if (engine.GetOfficeRequestPrompt() is { } office)
        {
            var person = snapshot.Officers.Single(p => p.Id == office.OfficerId);
            var ruler = snapshot.Officers.Single(p => p.Id == snapshot.Forces.Single(f => f.Id == response.Owner.ForceId).LeaderId);
            int distance = PersonnelRulebook.AffinityDistance(person, ruler);
            var random = AiPlanningContext.Create(AiObservation.FromSnapshot(response.Owner.ForceId, snapshot),
                $"response:office-request:{person.Id}", new(1, 0)).CreateAnalysisRandom();
            bool accept = random.NextUInt64() % 100 < (ulong)Math.Max(0, 100 - distance * distance);
            return new RespondOfficeRequestCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence,
                office.OfficerId, accept ? EventChoice.Accept : EventChoice.Reject);
        }
        if (engine.GetEmperorTransferPrompt() is { } emperor)
            return new RespondEmperorTransferCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence,
                emperor.EmperorId, emperor.CityId, EmperorTransferChoice.Support);
        if (engine.GetCountryTitlePrompt() is { } title)
        {
            var random = AiPlanningContext.Create(AiObservation.FromSnapshot(response.Owner.ForceId, snapshot),
                "response:country-title", new(1, 0)).CreateAnalysisRandom();
            int? selected = engine.GetAutomaticCountryTitle(response.Owner.ForceId, () => random.NextUInt64());
            if (selected is not int choice || !title.CandidateIds.Contains(choice)) Fail(response, "response-no-legal-option");
            return new ChooseCountryTitleCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, selected!.Value);
        }
        if (engine.GetAllianceParticipationPrompt() is { } alliance)
        {
            int target = alliance.Proposal.TargetForceId;
            return new AllianceParticipationCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, target,
                alliance.Proposal.EligibleMemberForceIds.Contains(response.Owner.ForceId));
        }
        if (response.Kind == "daily-task" && engine.GetDailyDecisionPrompt() is { } daily)
            return Daily(engine, id, response, snapshot.Simulation.NextSequence, daily);
        Fail(response, "response-unsupported");
        throw new InvalidOperationException("Unreachable response dispatch.");
    }

    private static RespondDailyTaskCommand Daily(SimulationEngine engine, Guid id, ResponseRequest request, long sequence, DailyDecisionPrompt prompt)
    {
        int force = request.Owner.ForceId;
        var response = new RespondDailyTaskCommand(id, force, sequence, prompt.TaskId, prompt.Step, false);
        if (prompt.Kind is "diplomacy-attack-request" or "diplomacy-prisoner-return")
            return engine.GetAutomaticDiplomacyResponse(id, force, sequence);
        if (engine.GetSearchRecruitmentPrompt() is not null) return response with { Accept = true };
        // The remaining search/study prompts require direct player control in Core.
        Fail(request, "response-unsupported");
        throw new InvalidOperationException("Unreachable response dispatch.");
    }

    internal static int ExecutionChance(CapturedRulerPrompt captive)
    {
        var a = captive.RulerAbilities;
        int best = Math.Max(Math.Max(a.War, a.Intelligence), Math.Max(a.Politics, a.Leadership));
        return Math.Max(0, 70 - captive.CaptorBaseTrust / 10 - best / 2 - captive.RelationValue);
    }

    private static void Fail(ResponseRequest response, string code, CommandRejection rejection = CommandRejection.None) =>
        throw new AutomaticResponseException(new(code, response.Kind, response.Owner.ControllerId, rejection));
}
