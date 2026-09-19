using San9.Core.Simulation;

namespace San9.Application.Tests;

internal static class DecisionResponses
{
    internal static SessionRequest Decline(GameSession session)
    {
        var state = session.Snapshot.Simulation;
        int force = state.PendingDailyTask?.RespondingForceId ?? state.PendingEvent!.RespondingForceId;
        var seat = state.Controls.Single(c => c.ForceId == force);
        var request = session.Request(new EndControlOperation(), seat.ControllerId);
        GameCommand command;
        if (state.PendingDailyTask is { } daily)
        {
            var task = state.Tasks.Single(t => t.Id == daily.RemainingTaskIds[0]);
            command = new RespondDailyTaskCommand(request.Id, force, state.NextSequence, task.Id, task.ResolutionStep, false);
        }
        else if (session.GetOfficeRequestPrompt() is { } officeRequest)
            command = new RespondOfficeRequestCommand(request.Id, force, state.NextSequence, officeRequest.OfficerId, EventChoice.Reject);
        else command = new AllianceParticipationCommand(request.Id, force, state.NextSequence, ((AllianceEventSession)state.PendingEvent!).Proposal.TargetForceId, false);
        return request with { Operation = new CommandOperation(command) };
    }

    internal static void DeclineAll(GameSession session)
    {
        while (session.Snapshot.Simulation.Boundary == SimulationBoundary.AwaitingDecision)
            Assert.Equal(SessionDisposition.Accepted, session.Apply(Decline(session)).Disposition);
    }
}
