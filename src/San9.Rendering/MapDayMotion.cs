using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Rendering;

/// <summary>Ten display positions, starting with the first displacement at the day's first callback.</summary>
public static class MapMovementCadence
{
    public const int StepMilliseconds = 100;
    public const int Steps = 10;
    public const int DayMilliseconds = StepMilliseconds * Steps;
    public const int ArrivalMilliseconds = StepMilliseconds * (Steps - 1);
    public static double Progress(double milliseconds) => milliseconds < 0 ? 0 :
        Math.Min(Steps, Math.Floor(milliseconds / StepMilliseconds) + 1) / Steps;
}

/// <summary>One day's display displacement, independent of the logical path and movement cost.</summary>
public sealed class MapUnitMotion
{
    public WorldPosition[] Points { get; }
    public bool HasDisplacement => Points[0] != Points[1];
    public MapUnitMotion(IEnumerable<UnitMovedEvent> movements)
    {
        var events = movements.ToArray();
        if (events.Length == 0) throw new ArgumentException("Daily movement requires an event.", nameof(movements));
        Points = [events[0].From, events[^1].To];
    }
    public IEnumerable<(double X, double Y)> Segment(double from, double to)
    {
        var first = Sample(from); var last = Sample(to);
        yield return (first.X, first.Y);
        yield return (last.X, last.Y);
    }
    public (double X, double Y, int Direction) Sample(double milliseconds)
    {
        double t = MapMovementCadence.Progress(milliseconds);
        var a = Points[0]; var b = Points[1];
        return (a.X + ((double)b.X - a.X) * t, a.Y + ((double)b.Y - a.Y) * t, UnitVisuals.Direction(a, b));
    }
}

public sealed record MapAttackMotion(int SourceId, bool SourceIsSettlement, int TargetId, bool TargetIsSettlement,
    double FromX, double FromY, double ToX, double ToY, int Direction, UnitVisualAction Action, MapProjectileKind Projectile)
{
    public int FactIndex { get; init; }
    public MapMotionPoint BodyPosition { get; init; }
    public int BodyDelayMilliseconds { get; init; }
    public int ProjectileDelayMilliseconds { get; init; }
    public int ShotSpacingMilliseconds { get; init; } = 200;
    public MapMotionPoint[] Path { get; init; } = [];
    public MapMotionPoint[] Origins { get; init; } = [];
}
public readonly record struct MapMotionPoint(double X, double Y);
public sealed record MapProjectileSprite(string AssetId, double X, double Y)
{
    public int Depth => MapProjectiles.VisualDepth;
}

public static class MapProjectiles
{
    public const int VisualDepth = 50000;
    private const int MinimumFlightMilliseconds = 256;
    private const int BallisticMillisecondsPerPixel = 2;
    private const int GroundMillisecondsPerPixel = 4;

    private readonly record struct Flight(MapMotionPoint Origin, MapMotionPoint Target, double Duration, double Height);

    public static int ShotCount(MapAttackMotion attack) => attack.Origins.Length > 0 ? attack.Origins.Length :
        attack.Projectile == MapProjectileKind.Arrow ? (attack.SourceIsSettlement ? 8 : 5) : 1;

    private static int Scatter(MapAttackMotion attack, int shot, int channel, int radius) =>
        (int)(UnitVisuals.Seed(attack.SourceId, attack.TargetId, shot * 5 + channel) % (uint)(radius * 2 + 1)) - radius;

    private static Flight PrepareFlight(MapAttackMotion attack, int shot)
    {
        var origin = attack.Origins.Length > shot ? attack.Origins[shot] : new MapMotionPoint(attack.FromX, attack.FromY);
        var target = new MapMotionPoint(attack.ToX, attack.ToY);
        bool ballistic = attack.Projectile is MapProjectileKind.Arrow or MapProjectileKind.Stone;
        if (ballistic)
        {
            target = new(target.X + Scatter(attack, shot, 2, 32), target.Y + Scatter(attack, shot, 3, 32));
        }
        double dx = target.X - origin.X, dy = target.Y - origin.Y;
        double duration = Math.Max(MinimumFlightMilliseconds, Math.Sqrt(dx * dx + dy * dy) *
            (ballistic ? BallisticMillisecondsPerPixel : GroundMillisecondsPerPixel));
        double height = attack.Projectile == MapProjectileKind.Rockfall ? 0 : Math.Sqrt(dx * dx + 4 * dy * dy) / 8;
        if (ballistic)
            height *= 0.85 + (UnitVisuals.Seed(attack.SourceId, attack.TargetId, shot * 5 + 4) % 31) / 100.0;
        return new(origin, target, duration, height);
    }

    public static IEnumerable<MapProjectileSprite> Sample(MapAttackMotion attack, MapProjectile art, double milliseconds,
        IReadOnlyDictionary<string, MapAnimationClip>? clips = null, IReadOnlySet<int>? shots = null)
    {
        for (int i = 0; i < ShotCount(attack); i++)
        {
            if (shots is not null && !shots.Contains(i)) continue;
            double elapsed = milliseconds - LaunchTime(attack, i);
            if (elapsed < 0) continue;
            var flight = PrepareFlight(attack, i);
            double dx = flight.Target.X - flight.Origin.X, dy = flight.Target.Y - flight.Origin.Y;
            double t = elapsed / flight.Duration;
            if (elapsed >= flight.Duration)
            {
                if (art.Impact is {} impact && clips is not null)
                {
                    var clip = clips[impact.ClipId];
                    int index = (int)((elapsed - flight.Duration) / clip.FrameMilliseconds);
                    if (index < clip.Frames.Length) yield return new(clip.Frames[index], flight.Target.X + impact.X, flight.Target.Y + impact.Y);
                }
                continue;
            }
            double x = flight.Origin.X + dx * t;
            double y = flight.Origin.Y + dy * t - 4 * flight.Height * t * (1 - t);
            if (attack.Path.Length > 1)
            {
                double step = t * (attack.Path.Length - 1);
                int index = Math.Min((int)step, attack.Path.Length - 2);
                var a = attack.Path[index]; var b = attack.Path[index + 1];
                x = a.X + (b.X - a.X) * (step - index);
                y = a.Y + (b.Y - a.Y) * (step - index);
            }
            int frame = art.Directional
                ? ((int)Math.Round(Math.Atan2(dy - 4 * flight.Height * (1 - 2 * t), dx) * art.Frames.Length / (2 * Math.PI)) + art.Frames.Length) % art.Frames.Length
                : (int)(elapsed / art.FrameMilliseconds) % art.Frames.Length;
            yield return new(art.Frames[frame], x - art.AnchorX,
                y - art.AnchorY);
        }
    }
    public static double Duration(MapAttackMotion attack)
    {
        return Enumerable.Range(0, ShotCount(attack))
            .Max(i => Duration(attack, i));
    }
    public static double LaunchTime(MapAttackMotion attack, int shot) =>
        attack.ProjectileDelayMilliseconds + shot * attack.ShotSpacingMilliseconds;
    public static double Duration(MapAttackMotion attack, int shot) => LaunchTime(attack, shot) + PrepareFlight(attack, shot).Duration;
    public static bool IsVisible(MapAttackMotion attack, int shot, MapPresentationBounds viewport)
    {
        var flight = PrepareFlight(attack, shot);
        double left = Math.Min(flight.Origin.X, flight.Target.X), right = Math.Max(flight.Origin.X, flight.Target.X);
        double top = Math.Min(flight.Origin.Y, flight.Target.Y) - flight.Height;
        double bottom = Math.Max(flight.Origin.Y, flight.Target.Y);
        if (attack.Path.Length > 0)
        {
            left = Math.Min(left, attack.Path.Min(p => p.X)); right = Math.Max(right, attack.Path.Max(p => p.X));
            top = Math.Min(top, attack.Path.Min(p => p.Y)); bottom = Math.Max(bottom, attack.Path.Max(p => p.Y));
        }
        return viewport.Intersects(left, top, right, bottom, 192, 96);
    }
}

public readonly record struct MapPresentationBounds(double X, double Y, double Width, double Height)
{
    public bool ContainsBody(MapMotionPoint point) => point.X >= X - 80 && point.X < X + Width + 80 &&
        point.Y >= Y - 80 && point.Y < Y + Height + 80;
    public bool Intersects(double left, double top, double right, double bottom, double horizontal, double vertical) =>
        left < X + Width + horizontal && right >= X - horizontal && top < Y + Height + vertical && bottom >= Y - vertical;
}
