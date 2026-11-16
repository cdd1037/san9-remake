using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Owns one UI lifetime's strategic entrances and initial committed feedback.</summary>
public sealed class StrategyEntrancePublication
{
    private readonly GameSession session;
    private readonly HashSet<int> introducedForces = [];
    private (int? Force, GameClock? Clock) contextPresented;
    private long? entrancePresented;

    public StrategyEntrancePublication(GameSession session, long? resumedEntrance = null)
    {
        this.session = session;
        // Rebinding a live strategy screen has no new entrance or initial feedback to publish.
        entrancePresented = resumedEntrance;
        contextPresented = (session.View.PlayerForceId, session.StrategicContext.Clock);
    }

    public ExecutionOutput? Read(long entrance, TextCatalog texts, CommandPresentation content, bool introduceNewGame)
    {
        if (entrance == entrancePresented || session.Snapshot.Simulation.Boundary != SimulationBoundary.Stable) return null;
        var view = session.View;
        var context = session.Messages(texts);
        var contextIdentity = (view.PlayerForceId, session.StrategicContext.Clock);
        List<CommandMessage> messages = [];
        bool introduce = introduceNewGame && view.PlayerForceId is int force && !introducedForces.Contains(force);
        if (content.Progress is { } progress)
        {
            if (introduce) messages.AddRange(NewGameIntroduction.Produce(view, context, texts, progress));
            messages.AddRange(StrategyEntryPresentation.Produce(view, context, texts, progress));
            if (session.StrategicContext.Clock is not null && contextPresented != contextIdentity)
                messages.AddRange(StrategicContext.Produce(session.StrategicContext, view, context, texts, progress));
        }
        var initial = entrancePresented is null
            ? ExecutionPublication.Read(session.InitialView, view, session.Messages(texts, session.InitialView.World),
                texts, content, session.InitialEvents, executionMap: false)
            : new ExecutionOutput([], []);
        // Commit publication ownership only after every projection succeeds, so failure can be retried.
        entrancePresented = entrance;
        contextPresented = contextIdentity;
        if (introduce && content.Progress is not null) introducedForces.Add(view.PlayerForceId!.Value);
        return new ExecutionOutput([new(messages, []), .. initial.Steps]) { FeedbackIds = initial.FeedbackIds };
    }
}
