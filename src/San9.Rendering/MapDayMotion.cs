using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Rendering;

/// <summary>A display-only path. It never searches again or writes interpolated positions to the world.</summary>
public sealed class MapUnitMotion
{
    public WorldPosition[] Points { get; }
    private readonly double[] _distances;
    public MapUnitMotion(IEnumerable<UnitMovedEvent> movements)
    {
        var events = movements.ToArray();
        Points = events.SelectMany(e => new[] { e.From }.Concat(e.Steps).Append(e.To)).DistinctAdjacent().ToArray();
        _distances = new double[Points.Length];
        for (int i = 1; i < Points.Length; i++)
            _distances[i] = _distances[i - 1] + Math.Sqrt(Math.Pow(Points[i].X - Points[i - 1].X, 2) + Math.Pow(Points[i].Y - Points[i - 1].Y, 2));
    }
    public (double X, double Y, int Direction) Sample(double progress)
    {
        if (Points.Length == 0) throw new InvalidOperationException("Empty movement.");
        if (Points.Length == 1) return (Points[0].X, Points[0].Y, 0);
        double distance = Math.Clamp(progress, 0, 1) * _distances[^1];
        int segment = 1;
        while (segment < Points.Length - 1 && _distances[segment] < distance) segment++;
        double t = (distance - _distances[segment - 1]) / (_distances[segment] - _distances[segment - 1]);
        var a = Points[segment - 1]; var b = Points[segment];
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, UnitVisuals.Direction(a, b));
    }
}

internal static class MotionPoints
{
    internal static IEnumerable<WorldPosition> DistinctAdjacent(this IEnumerable<WorldPosition> points)
    {
        WorldPosition? last = null;
        foreach (var p in points) if (p != last) { yield return p; last = p; }
    }
}

public sealed record MapAttackMotion(int SourceId, bool SourceIsSettlement, int TargetId, bool TargetIsSettlement,
    double FromX, double FromY, double ToX, double ToY, int Direction, UnitVisualAction Action, MapProjectileKind Projectile)
{
    public MapMotionPoint[] Path { get; init; } = [];
    public MapMotionPoint[] Origins { get; init; } = [];
}
public readonly record struct MapMotionPoint(double X, double Y);
public sealed record MapProjectileSprite(string AssetId, double X, double Y);

public static class MapProjectiles
{
    public static IEnumerable<MapProjectileSprite> Sample(MapAttackMotion attack, MapProjectile art, double milliseconds,
        IReadOnlyDictionary<string, MapAnimationClip>? clips = null)
    {
        int count = attack.Origins.Length > 0 ? attack.Origins.Length : attack.Projectile == MapProjectileKind.Arrow ? (attack.SourceIsSettlement ? 8 : 5) : 1;
        for (int i = 0; i < count; i++)
        {
            double elapsed = milliseconds - i * 200;
            var origin = attack.Origins.Length > i ? attack.Origins[i] : new MapMotionPoint(attack.FromX, attack.FromY);
            double dx = attack.ToX - origin.X, dy = attack.ToY - origin.Y;
            double duration = Math.Max(256, Math.Sqrt(dx * dx + dy * dy) * 4);
            double t = elapsed / duration;
            if (t < 0) continue;
            if (t >= 1)
            {
                if (art.Impact is {} impact && clips is not null)
                {
                    var clip = clips[impact.ClipId];
                    int index = (int)((elapsed - duration) / clip.FrameMilliseconds);
                    if (index < clip.Frames.Length) yield return new(clip.Frames[index], attack.ToX + impact.X, attack.ToY + impact.Y);
                }
                continue;
            }
            double height = attack.Projectile == MapProjectileKind.Rockfall ? 0 : Math.Sqrt(dx * dx + 4 * dy * dy) / 8;
            double x = origin.X + dx * t;
            double y = origin.Y + dy * t - 4 * height * t * (1 - t);
            if (attack.Path.Length > 1)
            {
                double step = t * (attack.Path.Length - 1);
                int index = Math.Min((int)step, attack.Path.Length - 2);
                var a = attack.Path[index]; var b = attack.Path[index + 1];
                x = a.X + (b.X - a.X) * (step - index);
                y = a.Y + (b.Y - a.Y) * (step - index);
            }
            int frame = art.Directional
                ? ((int)Math.Round(Math.Atan2(dy - 4 * height * (1 - 2 * t), dx) * art.Frames.Length / (2 * Math.PI)) + art.Frames.Length) % art.Frames.Length
                : (int)(elapsed / art.FrameMilliseconds) % art.Frames.Length;
            yield return new(art.Frames[frame], x - art.AnchorX,
                y - art.AnchorY);
        }
    }
    public static double Duration(MapAttackMotion attack)
    {
        if (attack.Origins.Length > 0)
            return attack.Origins.Select((p, i) => Math.Max(256, Math.Sqrt(Math.Pow(attack.ToX - p.X, 2) + Math.Pow(attack.ToY - p.Y, 2)) * 4) + i * 200).Max();
        double dx = attack.ToX - attack.FromX, dy = attack.ToY - attack.FromY;
        return Math.Max(256, Math.Sqrt(dx * dx + dy * dy) * 4) +
            (attack.Projectile == MapProjectileKind.Arrow ? (attack.SourceIsSettlement ? 7 : 4) * 200 : 0);
    }
}
