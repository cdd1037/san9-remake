using San9.Content.Interface;

namespace San9.Rendering;

public readonly record struct SpeechBounds(double X, double Y, double Width, double Height)
{
    public double Overlap(SpeechBounds other) => Math.Max(0, Math.Min(X + Width, other.X + other.Width) - Math.Max(X, other.X)) *
        Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y));
}

/// <summary>Four original anchors. Equal overlap retains the preferred direction; edges do not change direction.</summary>
public static class MapSpeechLayout
{
    public static MapSpeechPlacement FromUnitDirection(int direction) => direction is >= 0 and < 8
        ? (MapSpeechPlacement)(direction / 2) : throw new ArgumentOutOfRangeException(nameof(direction));
    public static SpeechBounds Bounds(double x, double y, double width, MapSpeechPlacement placement) => placement switch
    {
        MapSpeechPlacement.LeftBelow => new(x + 30 - width, y, width, 108),
        MapSpeechPlacement.LeftAbove => new(x + 30 - width, y - 100, width, 108),
        MapSpeechPlacement.RightAbove => new(x - 54, y - 100, width, 108),
        MapSpeechPlacement.RightBelow => new(x - 54, y, width, 108),
        _ => throw new ArgumentOutOfRangeException(nameof(placement))
    };

    public static SpeechBounds HitBounds(SpeechBounds bounds) => new(bounds.X + 38, bounds.Y + 20, bounds.Width - 54, 48);

    public static MapSpeechPlacement Choose(double x, double y, double width, MapSpeechPlacement preferred,
        IReadOnlyList<SpeechBounds> occupied)
    {
        double Cost(MapSpeechPlacement placement) => occupied.Sum(other => Bounds(x, y, width, placement).Overlap(other));
        var best = preferred;
        double cost = Cost(best);
        foreach (var placement in Enum.GetValues<MapSpeechPlacement>())
        {
            double candidate = Cost(placement);
            if (candidate >= cost) continue;
            cost = candidate; best = placement;
        }
        return best;
    }
}
