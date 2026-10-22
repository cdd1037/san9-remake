namespace San9.Rendering;

public enum DiplomacyGesture { Rest, Salute, Bow }

/// <summary>Salutes return through the sheet; bows finish at rest. Skipping never advances dialogue.</summary>
public sealed class DiplomacyAnimation
{
    public DiplomacyGesture Gesture { get; private set; }
    public int FrameCount { get; private set; }
    private int _milliseconds;
    private double _elapsed;
    public bool IsComplete => Gesture == DiplomacyGesture.Rest;
    public int Frame
    {
        get
        {
            if (IsComplete) return 0;
            int step = (int)(_elapsed * 1000 / _milliseconds);
            return Gesture == DiplomacyGesture.Salute && step >= FrameCount ? 2 * FrameCount - 2 - step : step;
        }
    }
    public void Play(DiplomacyGesture gesture, int frames, int milliseconds)
    {
        if (frames < 1 || milliseconds < 1) throw new ArgumentOutOfRangeException(nameof(frames));
        Gesture = gesture; FrameCount = frames; _milliseconds = milliseconds; _elapsed = 0;
    }
    public void Advance(double seconds)
    {
        if (IsComplete) return;
        _elapsed += Math.Max(0, seconds);
        int steps = Gesture == DiplomacyGesture.Salute ? 2 * FrameCount - 1 : FrameCount;
        if (_elapsed * 1000 >= steps * _milliseconds) Skip();
    }
    public void Skip() { Gesture = DiplomacyGesture.Rest; _elapsed = 0; }
}
