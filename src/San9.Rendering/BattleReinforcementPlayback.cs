using San9.Content.Interface;

namespace San9.Rendering;

public sealed record BattleReinforcementFrame(BattleSprite Sprite, float Opacity);

/// <summary>Samples the delayed illustration sequence and its stepped fade from the scene clock.</summary>
public static class BattleReinforcementPlayback
{
    public const int DelayMilliseconds = 3500;

    public static BattleReinforcementFrame? Sample(IReadOnlyList<BattleSprite> frames, double elapsedSeconds)
    {
        long elapsed = (long)Math.Floor(elapsedSeconds * 1000 + 1e-6) - DelayMilliseconds;
        if (elapsed < 0 || frames.Count == 0) return null;
        foreach (var frame in frames)
        {
            if (elapsed < frame.FrameMilliseconds) return new(frame, 1);
            elapsed -= frame.FrameMilliseconds;
        }

        // The last illustration remains on screen while alpha decreases by 32 every 100 ms.
        if (elapsed >= 700) return null;
        int steps = (int)(elapsed / 100) + 1;
        int alpha = Math.Max(0, 255 - steps * 32);
        return alpha == 0 ? null : new(frames[^1], alpha / 255f);
    }
}
