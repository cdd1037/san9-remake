namespace San9.Rendering;

public static class MinimapGeometry
{
    // Unit diamond: top=(.5,0), right=(1,.5), bottom=(.5,1), left=(0,.5).
    public static (double X, double Y) FromGrid(double x, double y) => ((x - y + 200) / 400, (x + y + 1) / 400);
    public static bool TryGrid(double u, double v, out double x, out double y)
    {
        x = 200 * (u + v) - 100.5; y = 200 * (v - u) + 99.5;
        return double.IsFinite(x) && double.IsFinite(y) && x >= -0.5 && x < 199.5 && y >= -0.5 && y < 199.5;
    }

    public static (double U, double V) FromWorld(double x, double y) => ((x - 96) / 12800, (y - 112) / 6400);

    public static (double U, double V)[] ClipViewport(double left, double top, double right, double bottom)
    {
        List<(double U, double V)> polygon = [(left, top), (right, top), (right, bottom), (left, bottom)];
        foreach (var (a, b, c) in new[] { (1d, 1d, -.5), (-1d, -1d, 1.5), (1d, -1d, .5), (-1d, 1d, .5) })
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
