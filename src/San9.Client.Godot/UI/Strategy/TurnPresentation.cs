using San9.Application;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public enum TurnScreen { Strategy, Confirmation, Entering, Preparing, Playing, Returning, AwaitingDecision, Presenting, Terminal, Failed }

/// <summary>The owner thread presents one committed step; isolated workers calculate the next.</summary>
public sealed class TurnPresentation(GameSession session)
{
    public const double TransitionDuration = 1.375;
    public int TitleFrame => Math.Clamp((int)((TransitionDuration - _remaining) / 0.125), 0, 3);
    public TurnScreen Screen { get; private set; } = TurnScreen.Strategy;
    public string? Error { get; private set; }
    public StrategySnapshot? Display { get; private set; }
    public TurnDayPresentation? Day { get; private set; }
    public Func<StrategySnapshot, StrategySnapshot, IReadOnlyList<DomainEvent>, TurnDayPresentation> CreateDay { get; init; } =
        (before, after, events) => new(before, after, events);
    public Func<bool>? CanPlayDay { get; set; }
    public Func<bool>? CanContinue { get; set; }
    public SessionResult? LastResult { get; private set; }
    public long Publication { get; private set; }
    public bool Paused { get; private set; }
    public bool IsComputing => _pending is { IsCompleted: false };
    public double Progress { get; private set; }
    public bool IsStrategy => Screen is TurnScreen.Strategy or TurnScreen.Confirmation;
    public bool CanAdvance => Screen == TurnScreen.Strategy && session.Snapshot.Simulation.Boundary == SimulationBoundary.Stable;
    public bool CanBrowseMap => Screen is TurnScreen.Strategy or TurnScreen.Playing;
    public bool CanCancelFailure => !_presentationFailed && session.Snapshot.Simulation.CanPersist && session.Snapshot.Clock.DayNumber == _startedDay;
    private GameSession.PendingSessionOperation? _pending;
    private SessionRequest? _request;
    private TurnScreen _retryScreen;
    private double _remaining;
    private long _startedDay;
    private bool _advancing;
    private bool _presentationFailed;

    public bool Request()
    {
        if (!CanAdvance) return false;
        Screen = TurnScreen.Confirmation;
        Error = null;
        return true;
    }

    public void Cancel()
    {
        if (Screen == TurnScreen.Confirmation || Screen == TurnScreen.Failed && CanCancelFailure)
        { Screen = TurnScreen.Strategy; Display = null; }
    }

    public void Confirm()
    {
        if (Screen == TurnScreen.Failed && _presentationFailed)
        {
            Error = null; _presentationFailed = false; Screen = TurnScreen.Playing;
            return;
        }
        if (Screen == TurnScreen.Failed && _request is not null)
        {
            Error = null;
            Screen = _retryScreen;
            _pending = session.StartOperation(_request);
            return;
        }
        if (Screen != TurnScreen.Confirmation) return;
        Screen = TurnScreen.Entering;
        _advancing = true;
        Display = session.View;
        _startedDay = session.Snapshot.Clock.DayNumber;
        Progress = 0;
        Paused = false;
        _remaining = TransitionDuration;
        Start(new PrepareAdvanceOperation());
    }

    public void TogglePause() { if (Screen == TurnScreen.Playing) Paused = !Paused; }

    /// <summary>Decision UI submits on the same background path; repeated clicks cannot enqueue another answer.</summary>
    public bool Respond(GameCommand command, string controllerId = "player")
    {
        if (Screen != TurnScreen.AwaitingDecision || _pending is not null) return false;
        _request = session.Request(new CommandOperation(command), controllerId, command.CommandId);
        _pending = session.StartOperation(_request);
        return true;
    }

    public bool SelectEnding(EndingOperation operation)
    {
        if (Screen!=TurnScreen.Terminal || _pending is not null || session.Ending is not null) return false;
        Start(operation); return true;
    }

    public void Update(double seconds)
    {
        if (Screen==TurnScreen.Terminal && _pending is not null) { Publish(); return; }
        if (Screen == TurnScreen.Presenting)
        {
            if (CanContinue?.Invoke() != false) Continue();
            return;
        }
        if (Screen == TurnScreen.Strategy)
        {
            if (session.Snapshot.Simulation.Boundary == SimulationBoundary.Terminal) Screen = TurnScreen.Terminal;
            else if (session.Snapshot.Simulation.Boundary == SimulationBoundary.AwaitingDecision)
            { _advancing = false; _startedDay = session.Snapshot.Clock.DayNumber; Screen = TurnScreen.AwaitingDecision; }
            return;
        }
        if (Screen == TurnScreen.Returning)
        {
            _remaining -= seconds;
            if (_remaining <= 0) { Screen = TurnScreen.Strategy; Display = null; }
            return;
        }
        if (Screen == TurnScreen.Entering)
        {
            _remaining -= seconds;
            if (_remaining > 0) return;
            Screen = TurnScreen.Preparing;
        }
        if (Screen == TurnScreen.Preparing)
        {
            if (Publish()) Continue();
            return;
        }
        if (Screen == TurnScreen.AwaitingDecision)
        {
            if (_pending is not null)
            {
                if (Publish()) Continue();
            }
            else if (session.Snapshot.Simulation.Boundary != SimulationBoundary.AwaitingDecision)
            {
                Display = session.View;
                Continue();
            }
            return;
        }
        if (Screen != TurnScreen.Playing) return;
        if (Paused)
        {
            if (Day is not null && CanPlayDay?.Invoke() != false) Day.Advance(seconds, paused: true);
            return;
        }
        if (_pending is not { IsCompleted: true }) return;
        // Progress follows the prepared day (a choice may stop before the date advances).
        // Publication is delayed until its presentation time; pause never launches another day.
        StrategySnapshot prepared;
        IReadOnlyList<DomainEvent> events;
        try { prepared = _pending.PreparedView; events = _pending.PreparedEvents; }
        catch { Publish(); return; }
        try
        {
            Day ??= CreateDay(Display!, prepared, events);
            if (CanPlayDay?.Invoke() == false) return;
            Day.Advance(seconds);
            double completed = Display!.World.Clock.DayNumber - _startedDay;
            double next = _pending.PreparedSnapshot.Clock.DayNumber - _startedDay;
            int step = Math.Clamp((int)Math.Floor(Day.Progress * 10), 0, 10);
            Progress = Math.Clamp((completed + (next - completed) * step / 10) / 10, 0, 1);
        }
        catch (Exception error)
        {
            // Retain the prepared operation: a presentation error cannot publish an unseen day.
            Error = error.Message; _presentationFailed = true; Screen = TurnScreen.Failed;
            return;
        }
        if (Day?.Progress >= 1 && Publish()) Continue();
    }

    private void Start(SessionOperation operation)
    {
        _request = session.Request(operation);
        _pending = session.StartOperation(_request);
    }

    private bool Publish()
    {
        if (_pending is not { IsCompleted: true }) return false;
        try
        {
            var result = _pending.Complete();
            if (result.Disposition != SessionDisposition.Accepted)
                throw new InvalidOperationException(result.Rejection.ToString());
            Display = session.View;
            LastResult = result;
            Publication++;
            Progress = Math.Clamp((Display.World.Clock.DayNumber - _startedDay) / 10.0, 0, 1);
            return true;
        }
        catch (Exception error)
        {
            Error = error.Message;
            _retryScreen = Screen;
            Screen = TurnScreen.Failed;
            return false;
        }
        finally { _pending = null; Day = null; }
    }

    private void Continue()
    {
        if (CanContinue?.Invoke() == false) { Screen = TurnScreen.Presenting; return; }
        var state = session.Snapshot.Simulation;
        if (state.Boundary == SimulationBoundary.Terminal) { Screen = TurnScreen.Terminal; return; }
        if (state.Boundary == SimulationBoundary.AwaitingDecision) { Screen = TurnScreen.AwaitingDecision; return; }
        if (!_advancing) { Screen = TurnScreen.Strategy; Display = null; return; }
        if (session.Snapshot.Clock.DayNumber >= _startedDay + 10)
        { Screen = TurnScreen.Returning; _remaining = TransitionDuration; return; }
        if (state.ActiveControl < state.Controls.Length)
        {
            Screen = TurnScreen.Preparing;
            Start(new PrepareAdvanceOperation());
            return;
        }
        Screen = TurnScreen.Playing;
        Start(new AdvanceDayOperation());
    }
}
