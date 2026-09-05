using San9.Content.Maps;

namespace San9.Rendering;

public static class MapCameraBoundary
{
    public static (double X, double Y) Clamp(double px, double py, IReadOnlyList<MapSquare> squares)
    {
        // Search the local height surface, independent of resource image bounds.
        MapGeometry.TrySquare(px, py, out int x, out int y);
        for (int row = Math.Max(0, y - 4); row <= Math.Min(199, y + 4); row++)
        for (int column = Math.Max(0, x - 4); column <= Math.Min(199, x + 4); column++)
        {
            var square = squares[column + row * 200];
            var center = MapGeometry.Center(column, row);
            double dx = px - center.X;
            if (Math.Abs(dx) > 32) continue;
            int mask = square.ElevationMask;
            double side = (mask & (dx < 0 ? 4 : 8)) != 0 ? -10 : 0;
            double top = -16 - ((mask & 1) != 0 ? 10 : 0);
            double bottom = 16 - ((mask & 2) != 0 ? 10 : 0);
            double t = Math.Abs(dx) / 32;
            double cy = center.Y - 10 * (square.Elevation - 1);
            if (py >= cy + top + (side - top) * t && py < cy + bottom + (side - bottom) * t)
                return (px, py);
        }
        // Constrain continuously to the perimeter; never snap the window to a square center.
        double best = double.PositiveInfinity;
        (double X, double Y) result = default;
        void Edge(double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0, 1);
            double qx = ax + t * vx, qy = ay + t * vy;
            double distance = (qx - px) * (qx - px) + (qy - py) * (qy - py);
            if (distance < best) { best = distance; result = (qx, qy); }
        }
        void Square(int column, int row)
        {
            var square = squares[column + row * 200];
            var center = MapGeometry.Center(column, row);
            double cy = center.Y - 10 * (square.Elevation - 1);
            int mask = square.ElevationMask;
            double top = cy - 16 - ((mask & 1) != 0 ? 10 : 0);
            double bottom = cy + 16 - ((mask & 2) != 0 ? 10 : 0);
            double left = cy - ((mask & 4) != 0 ? 10 : 0);
            double right = cy - ((mask & 8) != 0 ? 10 : 0);
            if (column == 0) Edge(center.X - 32, left, center.X, top);
            if (row == 0) Edge(center.X, top, center.X + 32, right);
            if (column == 199) Edge(center.X + 32, right, center.X, bottom);
            if (row == 199) Edge(center.X, bottom, center.X - 32, left);
        }
        for (int i = 0; i < 200; i++)
        {
            Square(i, 0); Square(i, 199); Square(0, i); Square(199, i);
        }
        return result;
    }
}
