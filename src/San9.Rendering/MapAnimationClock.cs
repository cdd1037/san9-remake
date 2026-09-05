using San9.Content.Maps;

namespace San9.Rendering;

public static class MapAnimationClock
{
    public static string Asset(MapAnimationClip clip, string season, double elapsedMilliseconds, int phase = 0)
    {
        if (season is not ("spring" or "summer" or "autumn" or "winter")) throw new ArgumentException("Unknown season.", nameof(season));
        var frames = clip.SeasonFrames.Count == 0 ? clip.Frames : clip.SeasonFrames[season];
        return frames[Frame(clip, elapsedMilliseconds, phase)];
    }

    public static int Frame(MapAnimationClip clip, double elapsedMilliseconds, int phase = 0)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0 || clip.FrameMilliseconds <= 0 || clip.Frames.Length == 0 || phase < 0 || phase >= clip.Frames.Length)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        return (int)((Math.Floor(elapsedMilliseconds / clip.FrameMilliseconds) % clip.Frames.Length + phase) % clip.Frames.Length);
    }
}
