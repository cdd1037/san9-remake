using San9.Content.Interface;

namespace San9.Rendering;

/// <summary>Only animation time is consumed here. Entering and leaving never advance a text page.</summary>
public sealed class EndingSceneAnimation(double enterSeconds, double scrollSeconds, double holdSeconds)
{
    private double _elapsed;
    private bool _leaving;
    public bool Reading => !_leaving && _elapsed >= enterSeconds;
    public bool Complete => _leaving && _elapsed >= scrollSeconds + holdSeconds;
    public double Opacity => enterSeconds <= 0 || _leaving ? 1 : Math.Clamp(_elapsed / enterSeconds, 0, 1);
    public double ScrollProgress => !_leaving ? 0 : scrollSeconds <= 0 ? 1 : Math.Clamp(_elapsed / scrollSeconds, 0, 1);
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _elapsed += seconds;
    }
    public void SkipEntrance() { if (!_leaving) _elapsed = Math.Max(_elapsed, enterSeconds); }
    public void Leave() { if (_leaving) return; _leaving = true; _elapsed = 0; }
    public void SkipExit() { if (_leaving) _elapsed = scrollSeconds + holdSeconds; }

    public static double ScrollX(EndingScrollLayer layer, double seconds, double pixelsPerSecond)
    {
        double width = layer.MaximumX - layer.MinimumX;
        double distance = layer.MaximumX - layer.Sprite.X + Math.Floor(seconds * pixelsPerSecond);
        double remainder = distance % width;
        // The native loop includes its starting coordinate, then wraps on reaching the minimum.
        return layer.MaximumX - remainder;
    }
}
