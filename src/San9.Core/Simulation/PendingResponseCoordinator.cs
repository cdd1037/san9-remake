using San9.Core.Domain;

namespace San9.Core.Simulation;

internal readonly record struct ResponseResolution(CommandRejection Rejection, GameClock? ResumeFrom = null);

/// <summary>Resolves mandatory decisions and selects the pipeline stage to resume.</summary>
internal static class PendingResponseCoordinator
{
    internal static bool IsResponse(GameCommand command) => command is
        ChooseSnipingSuccessorCommand or ChooseCapturedRulerDispositionCommand or RespondEmperorTransferCommand or
        ChooseCountryTitleCommand or RespondOfficeRequestCommand or ChooseDuelSuccessorCommand or
        AllianceParticipationCommand or RespondDailyTaskCommand or ChooseExecutionSuccessorCommand or ChooseTacticStudentCommand;

    internal static ResponseResolution Resolve(WorldMutation mutation, GameCommand command,
        SimulationRules rules, ResolutionPipeline pipeline, List<DomainEvent> events)
    {
        GameClock? resume = null;
        switch (command)
        {
            case ChooseTacticStudentCommand student:
                var studyRejection = TacticStudy.Respond(mutation, student, rules, events);
                if (studyRejection != CommandRejection.None) return new(studyRejection);
                if (mutation.Simulation.PendingTacticStudy is null)
                    pipeline.Prepare(mutation, events, ResolutionStage.QuarterStudyOffers);
                break;
            case ChooseSnipingSuccessorCommand snipingSuccessor:
                var sniping = mutation.Simulation.PendingSniping!;
                if (sniping.RulerId != snipingSuccessor.RulerId || snipingSuccessor.SuccessorId is int heir &&
                    !RulerSuccession.Candidates(mutation, sniping.RulerId).Contains(heir)) return new(CommandRejection.InvalidDecision);
                int snipingHeir = snipingSuccessor.SuccessorId ?? RulerSuccession.Automatic(mutation,
                    sniping.RespondingForceId, mutation.Officers[sniping.RulerId])!.Id;
                pipeline.ResumeSniping(mutation, events, snipingHeir);
                resume = sniping.Exchange!.XunStarted;
                break;
            case ChooseDuelSuccessorCommand duelSuccessor:
                var duel = mutation.Simulation.PendingDuel!;
                if (duel.Aftermath.PendingRulerId != duelSuccessor.RulerId ||
                    duelSuccessor.SuccessorId is int selected && !DuelAftermath.Successors(mutation, duelSuccessor.RulerId).Contains(selected))
                    return new(CommandRejection.InvalidDecision);
                int duelHeir = duelSuccessor.SuccessorId ?? RulerSuccession.Automatic(mutation,
                    duel.RespondingForceId, mutation.Officers[duelSuccessor.RulerId])!.Id;
                pipeline.ResumeDuel(mutation, events, duelHeir);
                resume = duel.XunStarted;
                break;
            case ChooseCapturedRulerDispositionCommand disposition:
                var captive = mutation.Simulation.PendingCapturedRuler!;
                if (disposition.RulerId != captive.RulerId || !Enum.IsDefined(disposition.Action)) return new(CommandRejection.InvalidDecision);
                mutation.SetSimulation(mutation.Simulation with { PendingCapturedRuler = null, Boundary = SimulationBoundary.Stable });
                CapturedRulerDisposition.Resolve(mutation, rules, events, captive, disposition.Action);
                if (!mutation.Simulation.HasPendingDailyResolution)
                    pipeline.FinishUnitAndBuildingEvents(mutation, events, captive.XunStarted, captive.RulerId);
                resume = captive.XunStarted;
                break;
            case ChooseExecutionSuccessorCommand successor:
                var executionPending = mutation.Simulation.PendingExecutionSuccession!;
                var successionRejection = PrisonerExecution.Respond(mutation, successor, rules, events);
                if (successionRejection != CommandRejection.None) return new(successionRejection);
                if (executionPending.XunStarted is { } executionStart)
                {
                    pipeline.FinishUnitAndBuildingEvents(mutation, events, executionStart, executionPending.RulerId);
                    resume = executionStart;
                }
                break;
            case RespondDailyTaskCommand response:
                var waiting = mutation.Simulation.PendingDailyTask!;
                if (waiting.RemainingTaskIds[0] != response.TaskId || response.Step !=
                    mutation.Simulation.Tasks.Single(t => t.Id == response.TaskId).ResolutionStep)
                    return new(CommandRejection.InvalidDecision);
                var respondingTask = mutation.Simulation.Tasks.Single(t => t.Id == response.TaskId);
                if (response.MerchantInvestment is not null && respondingTask.SearchEncounter is not MerchantInvestmentEncounter ||
                    response.CountryTitleId is not null && respondingTask.SearchEncounter is not ShanyangGongEncounter ||
                    !rules.FindTask(respondingTask.Kind)!.AcceptsResponse(mutation, respondingTask, response))
                    return new(CommandRejection.InvalidDecision);
                pipeline.Respond(mutation, response, events);
                resume = waiting.XunStarted;
                break;
            case RespondEmperorTransferCommand emperorTransfer:
                var emperorRejection = EmperorTransfer.Respond(mutation, rules, emperorTransfer, events);
                if (emperorRejection != CommandRejection.None) return new(emperorRejection);
                pipeline.Prepare(mutation, events, ResolutionStage.XunCleanup);
                break;
            case ChooseCountryTitleCommand countryTitle:
                var titleRejection = PoliticalEvents.Respond(mutation, rules, countryTitle, events);
                if (titleRejection != CommandRejection.None) return new(titleRejection);
                pipeline.Prepare(mutation, events, ResolutionStage.QuarterPolitics);
                break;
            case RespondOfficeRequestCommand officeRequest:
                var officeRejection = OfficeRequests.Respond(mutation, rules, officeRequest, events);
                if (officeRejection != CommandRejection.None) return new(officeRejection);
                if (mutation.Simulation.PendingEvent is null)
                    pipeline.Prepare(mutation, events, ResolutionStage.XunUpdate);
                break;
            case AllianceParticipationCommand participation:
                var participationRejection = AllianceFormation.Respond(mutation, participation, rules, events);
                if (participationRejection != CommandRejection.None) return new(participationRejection);
                if (mutation.Simulation.PendingEvent is null)
                    pipeline.Prepare(mutation, events, ResolutionStage.QuarterEvents);
                break;
            default: throw new ArgumentException("Not a response command.", nameof(command));
        }
        return new(CommandRejection.None, resume);
    }
}
