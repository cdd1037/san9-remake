namespace San9.Client.Godot.Settings;

/// <summary>Presentation time starts when the current page's resources are ready.</summary>
public sealed class MessagePageTimer
{
    private double _elapsed;
    public void Reset() => _elapsed = 0;
    public bool Advance(double seconds, MessageSpeed speed, bool ready)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Enum.IsDefined(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        if (!ready || speed == MessageSpeed.Pause) return false;
        _elapsed += seconds;
        return _elapsed + 1e-9 >= (speed == MessageSpeed.Fast ? 2 : 4);
    }
}
