namespace San9.Rendering;

public readonly record struct OverlayBounds(double X, double Y, double Width, double Height)
{
    public double Overlap(OverlayBounds other) => Math.Max(0, Math.Min(X + Width, other.X + other.Width) - Math.Max(X, other.X)) *
        Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y));

    public OverlayBounds Fit(OverlayBounds viewport) => this with
    {
        X = Math.Clamp(X, viewport.X, viewport.X + Math.Max(0, viewport.Width - Width)),
        Y = Math.Clamp(Y, viewport.Y, viewport.Y + Math.Max(0, viewport.Height - Height))
    };
}

/// <summary>Fits an overlay to the viewport, then minimizes overlap with visible UI without scaling it.</summary>
public static class OverlayLayout
{
    public static OverlayBounds Place(OverlayBounds desired, OverlayBounds viewport, IReadOnlyList<OverlayBounds> occupied)
    {
        var initial = desired.Fit(viewport);
        var xs = new List<double> { initial.X, viewport.X, viewport.X + viewport.Width - desired.Width };
        var ys = new List<double> { initial.Y, viewport.Y, viewport.Y + viewport.Height - desired.Height };
        foreach (var obstacle in occupied)
        {
            xs.Add(obstacle.X - desired.Width); xs.Add(obstacle.X + obstacle.Width);
            ys.Add(obstacle.Y - desired.Height); ys.Add(obstacle.Y + obstacle.Height);
        }
        (double Overlap, double Distance) Cost(OverlayBounds rect) =>
            (occupied.Sum(rect.Overlap), Math.Abs(rect.X - desired.X) + Math.Abs(rect.Y - desired.Y));
        var best = initial;
        var cost = Cost(best);
        if (cost.Overlap == 0) return best;
        foreach (double x in xs.Distinct())
        foreach (double y in ys.Distinct())
        {
            var candidate = (desired with { X = x, Y = y }).Fit(viewport);
            var next = Cost(candidate);
            if (next.CompareTo(cost) >= 0) continue;
            best = candidate; cost = next;
        }
        return best;
    }
}
