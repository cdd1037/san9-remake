namespace San9.Rendering;

public sealed class MinimapGeometry(int width, int height)
{
    public int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    private double Span => (double)Width + Height;

    public (double X, double Y) FromGrid(double x, double y) => ((x - y + Height) / Span, (x + y + 1) / Span);
    public bool TryGrid(double u, double v, out double x, out double y)
    {
        x = (Span * (u + v) - Height - 1) / 2; y = (Span * (v - u) + Height - 1) / 2;
        return double.IsFinite(x) && double.IsFinite(y) && x >= -0.5 && x < Width - 0.5 && y >= -0.5 && y < Height - 0.5;
    }

    public (double X, double Y) ToWorld(double x, double y) => (32 * (x + Height - y) + 96, 16 * (x + y + 1) + 112);
    public (double U, double V) FromWorld(double x, double y) => ((x - 96) / (32 * Span), (y - 112) / (16 * Span));

    public (double U, double V)[] ClipViewport(double left, double top, double right, double bottom)
    {
        List<(double U, double V)> polygon = [(left, top), (right, top), (right, bottom), (left, bottom)];
        foreach (var (a, b, c) in new[] { (1d, 1d, -Height / Span), (-1d, -1d, (2d * Width + Height) / Span),
            (1d, -1d, Height / Span), (-1d, 1d, Height / Span) })
        {
            if (polygon.Count == 0) break;
            List<(double U, double V)> clipped = [];
            var previous = polygon[^1]; double previousDistance = a * previous.U + b * previous.V + c;
            foreach (var current in polygon)
            {
                double distance = a * current.U + b * current.V + c;
                if ((distance >= 0) != (previousDistance >= 0))
                {
                    double t = previousDistance / (previousDistance - distance);
                    clipped.Add((previous.U + t * (current.U - previous.U), previous.V + t * (current.V - previous.V)));
                }
                if (distance >= 0) clipped.Add(current);
                previous = current; previousDistance = distance;
            }
            polygon = clipped;
        }
        return polygon.ToArray();
    }
}
