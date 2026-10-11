using System.Collections.Immutable;

namespace San9.Rendering;

public sealed record BattleProjectileFrame(double X, double Y, int Direction);
public static class BattleProjectilePath
{
    public static ImmutableArray<BattleProjectileFrame> Prepare(double x, double y, double targetX, double targetY, int steps, bool flat, Func<int, int> next)
    {
        var frames = ImmutableArray.CreateBuilder<BattleProjectileFrame>(steps + 1);
        double dx = targetX - x, dy = targetY - y;
        double span = Math.Max(4, Math.Sqrt(dx * dx + 4 * dy * dy));
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double lift = (flat ? .5 : span / 8) * (80 + next(41)) / 100;
            double slope = dy / 2 - 4 * lift * (1 - 2 * t);
            frames.Add(new(Math.Truncate(x + dx * t), Math.Truncate(y + dy * t - 4 * lift * t * (1 - t)), Direction(dx, slope)));
        }
        return frames.MoveToImmutable();
    }
    public static int Direction(double dx, double slope)
    {
        // Uneven artwork sectors are centred on frame 3 for east.
        double ratio = Math.Abs(dx) > 0 ? Math.Abs(slope / dx) : double.PositiveInfinity;
        int sector = ratio < .25 ? 0 : ratio < .75 ? 1 : ratio < 4.0 / 3 ? 2 : ratio < 4 ? 3 : 4;
        return ((dx >= 0 ? 3 + (slope >= 0 ? sector : -sector) : 11 + (slope >= 0 ? -sector : sector)) + 16) % 16;
    }
}
