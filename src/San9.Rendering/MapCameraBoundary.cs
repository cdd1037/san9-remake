using San9.Content.Maps;
using static San9.Content.Maps.MapDisplayMetrics;

namespace San9.Rendering;

public static class MapCameraBoundary
{
    private readonly record struct Segment(double AX, double AY, double BX, double BY);

    /// <summary>Consumes relative travel along the surface and its boundary without losing tangential speed.</summary>
    public static (double X, double Y) Move(double px, double py, double dx, double dy, IReadOnlyList<MapSquare> squares, int width, int height)
    {
        if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(dx) || !double.IsFinite(dy))
            throw new ArgumentOutOfRangeException(nameof(dx));
        double remaining = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(remaining)) throw new ArgumentOutOfRangeException(nameof(dx));
        (px, py) = Clamp(px, py, squares, width, height);
        const double epsilon = 1e-7;
        if (remaining <= epsilon) return (px, py);
        double ux = dx / remaining, uy = dy / remaining;
        var edges = Perimeter(squares, width, height).ToArray();
        // Every slide has positive progress along the requested direction, so an edge cannot be revisited.
        for (int step = 0; step <= edges.Length && remaining > epsilon; step++)
        {
            double hit = double.PositiveInfinity;
            foreach (var edge in edges)
            {
                double ex = edge.BX - edge.AX, ey = edge.BY - edge.AY;
                double cross = ux * ey - uy * ex;
                // Perimeter edges run clockwise in screen coordinates. Only exits block travel.
                if (cross <= epsilon) continue;
                double ax = edge.AX - px, ay = edge.AY - py;
                double distance = (ax * ey - ay * ex) / cross;
                double along = (ax * uy - ay * ux) / cross;
                if (distance >= -epsilon && distance <= remaining && along >= -epsilon && along <= 1 + epsilon)
                    hit = Math.Min(hit, Math.Max(0, distance));
            }
            if (double.IsPositiveInfinity(hit)) return Clamp(px + ux * remaining, py + uy * remaining, squares, width, height);
            px += ux * hit; py += uy * hit; remaining -= hit;
            if (remaining <= epsilon) break;

            double best = epsilon, tx = 0, ty = 0, available = 0;
            foreach (var edge in edges)
            {
                double ex = edge.BX - edge.AX, ey = edge.BY - edge.AY;
                double length = Math.Sqrt(ex * ex + ey * ey);
                double along = ((px - edge.AX) * ex + (py - edge.AY) * ey) / length;
                double normal = ((px - edge.AX) * ey - (py - edge.AY) * ex) / length;
                if (Math.Abs(normal) > epsilon || along < -epsilon || along > length + epsilon) continue;
                double dot = (ux * ex + uy * ey) / length;
                double travel = dot >= 0 ? length - along : along;
                if (travel <= epsilon || Math.Abs(dot) <= best) continue;
                best = Math.Abs(dot); available = travel;
                double sign = Math.Sign(dot);
                tx = sign * ex / length; ty = sign * ey / length;
            }
            // At a corner or a perpendicular wall, do not invent a direction or bank leftover distance.
            if (available <= epsilon) break;
            double slide = Math.Min(remaining, available);
            px += tx * slide; py += ty * slide; remaining -= slide;
        }
        return Clamp(px, py, squares, width, height);
    }

    public static (double X, double Y) Clamp(double px, double py, IReadOnlyList<MapSquare> squares, int width, int height)
    {
        // Search the local height surface, independent of resource image bounds.
        MapGeometry.TrySquare(px, py, out int x, out int y, width, height);
        for (int row = Math.Max(0, y - 4); row <= Math.Min((height - 1), y + 4); row++)
        for (int column = Math.Max(0, x - 4); column <= Math.Min((width - 1), x + 4); column++)
        {
            var square = squares[column + row * width];
            var center = MapGeometry.Center(column, row, height);
            double dx = px - center.X;
            if (Math.Abs(dx) > HalfCellWidth) continue;
            int mask = square.ElevationMask;
            double side = (mask & (dx < 0 ? 4 : 8)) != 0 ? -ElevationStep : 0;
            double top = -HalfCellHeight - ((mask & 1) != 0 ? ElevationStep : 0);
            double bottom = HalfCellHeight - ((mask & 2) != 0 ? ElevationStep : 0);
            double t = Math.Abs(dx) / HalfCellWidth;
            double cy = center.Y - ElevationStep * (square.Elevation - 1);
            if (py >= cy + top + (side - top) * t && py < cy + bottom + (side - bottom) * t)
                return (px, py);
        }
        // Constrain continuously to the perimeter; never snap the window to a square center.
        double best = double.PositiveInfinity;
        (double X, double Y) result = default;
        foreach (var edge in Perimeter(squares, width, height))
        {
            var (ax, ay, bx, by) = edge;
            double vx = bx - ax, vy = by - ay;
            double t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0, 1);
            double qx = ax + t * vx, qy = ay + t * vy;
            double distance = (qx - px) * (qx - px) + (qy - py) * (qy - py);
            if (distance < best) { best = distance; result = (qx, qy); }
        }
        return result;
    }

    private static IEnumerable<Segment> Perimeter(IReadOnlyList<MapSquare> squares, int width, int height)
    {
        IEnumerable<Segment> Square(int column, int row)
        {
            var square = squares[column + row * width];
            var center = MapGeometry.Center(column, row, height);
            double cy = center.Y - ElevationStep * (square.Elevation - 1);
            int mask = square.ElevationMask;
            double top = cy - HalfCellHeight - ((mask & 1) != 0 ? ElevationStep : 0);
            double bottom = cy + HalfCellHeight - ((mask & 2) != 0 ? ElevationStep : 0);
            double left = cy - ((mask & 4) != 0 ? ElevationStep : 0);
            double right = cy - ((mask & 8) != 0 ? ElevationStep : 0);
            if (column == 0) yield return new(center.X - HalfCellWidth, left, center.X, top);
            if (row == 0) yield return new(center.X, top, center.X + HalfCellWidth, right);
            if (column == width - 1) yield return new(center.X + HalfCellWidth, right, center.X, bottom);
            if (row == height - 1) yield return new(center.X, bottom, center.X - HalfCellWidth, left);
        }
        for (int x = 0; x < width; x++)
        {
            foreach (var edge in Square(x, 0)) yield return edge;
            if (height > 1)
                foreach (var edge in Square(x, height - 1)) yield return edge;
        }
        for (int y = 1; y < height - 1; y++)
        {
            foreach (var edge in Square(0, y)) yield return edge;
            if (width > 1)
                foreach (var edge in Square(width - 1, y)) yield return edge;
        }
    }
}
