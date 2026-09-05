namespace San9.Rendering;

public static class MapGeometry
{
    public static (int X, int Y) Center(int x, int y) => (32 * (x + 200 - y) + 96, 16 * (x + y + 1) + 112);
    public static bool TrySquare(double px, double py, out int x, out int y)
    {
        double difference = (px - 96) / 32 - 200;
        double sum = (py - 112) / 16 - 1;
        x = (int)Math.Floor((sum + difference) / 2 + 0.5);
        y = (int)Math.Floor((sum - difference) / 2 + 0.5);
        return x is >= 0 and < 200 && y is >= 0 and < 200;
    }
    public static int Mod(int value, int period) => (value % period + period) % period;
}
