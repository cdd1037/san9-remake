namespace San9.Client.Godot;

// Position phase survives display snapshots; a day's progress owns its pause boundary.
public sealed class UnitPositionClock
{
    private TurnDayPresentation? _day;
    private double _elapsed;
    public double Milliseconds { get; private set; }

    public void Advance(double seconds, TurnDayPresentation? day)
    {
        if (_day is not null && !ReferenceEquals(day, _day))
            Milliseconds += Math.Max(0, _day.Elapsed - _elapsed) * 1000;
        double advance = day is null ? (_day is null ? Math.Max(0, seconds) : 0) :
            Math.Max(0, day.Elapsed - (ReferenceEquals(day, _day) ? _elapsed : 0));
        Milliseconds += advance * 1000;
        _day = day;
        _elapsed = day?.Elapsed ?? 0;
    }
}
