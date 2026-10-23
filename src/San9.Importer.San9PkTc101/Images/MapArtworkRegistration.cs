namespace San9.Importer.San9PkTc101.Images;

public sealed record MapArtworkAnchor(int X, int Y, int SourceX, int SourceY);

/// <summary>Registers baked map art to authored landmarks without publishing a second city coordinate table.</summary>
public static class MapArtworkRegistration
{
    public static IndexedImage Align(IndexedImage source, IReadOnlyList<MapArtworkAnchor> anchors)
    {
        if (anchors.Count == 0) return source;
        byte[] pixels = new byte[source.Pixels.Length];
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            double sum = 0, dx = 0, dy = 0;
            foreach (var anchor in anchors)
            {
                double distance = (double)(x - anchor.X) * (x - anchor.X) + (double)(y - anchor.Y) * (y - anchor.Y);
                if (distance == 0) { dx = anchor.SourceX - anchor.X; dy = anchor.SourceY - anchor.Y; sum = 1; break; }
                double weight = 1 / (distance * distance);
                sum += weight; dx += weight * (anchor.SourceX - anchor.X); dy += weight * (anchor.SourceY - anchor.Y);
            }
            int sx = Math.Clamp((int)Math.Round(x + dx / sum), 0, source.Width - 1);
            int sy = Math.Clamp((int)Math.Round(y + dy / sum), 0, source.Height - 1);
            pixels[y * source.Width + x] = source.Pixels[sy * source.Width + sx];
        }
        return new(source.Width, source.Height, pixels);
    }
}
