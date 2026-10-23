using San9.Content.Images;

namespace San9.Importer.San9PkTc101.Images;

/// <summary>Separates the framed island inset from its source mainland coastline.</summary>
internal static class OpeningIslandArtwork
{
    internal static PngImage RemovePreviewSeaFrame(PngImage background)
    {
        // G_Start2's preview uses green mainland/outer decoration and blue ink for
        // the inset sea panel. Drop the panel coverage rather than paint over it;
        // the opening page supplies the sea beneath this transparent artwork.
        byte[] pixels = (byte[])background.Pixels.Clone();
        for (int p = 0; p < pixels.Length; p += 4)
            if (pixels[p + 2] > pixels[p + 1]) pixels[p + 3] = 0;
        IndexedImageArchive.ExtendTransparentColors(pixels, background.Width, background.Height);
        return background with { Pixels = pixels };
    }

    public static (PngImage Background, PngImage Island) Separate(PngImage background, PngImage inset,
        int x, int y, int centerX, int centerY, IndexedImage? cityMask = null)
    {
        if (background.Channels != 4 || inset.Channels != 4) throw new InvalidDataException("Opening layers require RGBA.");
        int width = inset.Width, height = inset.Height;
        var land = new HashSet<int>();
        Queue<int> pending = new(); pending.Enqueue(centerY * width + centerX);
        if (cityMask is not null)
            for (int py = 0; py < height; py++)
            for (int px = 0; px < width; px++)
                if (cityMask.Pixels[(py + y) * cityMask.Width + px + x] == 46 ||
                    px >= 30 && py >= 76 && py < 152 && inset.Pixels[(py * width + px) * 4] == inset.Pixels[(py * width + px) * 4 + 1] &&
                    inset.Pixels[(py * width + px) * 4 + 1] == inset.Pixels[(py * width + px) * 4 + 2]) land.Add(py * width + px);
        // The preview includes a green decorative margin touching the island. These
        // are source-art crop bounds, not another city position or gameplay area.
        while (cityMask is null && pending.TryDequeue(out int index))
        {
            int p = index * 4;
            if (index % width < 88 || index % width >= 118 || index / width < 52 || index / width >= 116 ||
                land.Contains(index) || inset.Pixels[p + 3] == 0 || inset.Pixels[p + 1] < 32 ||
                inset.Pixels[p + 1] < inset.Pixels[p] || inset.Pixels[p + 1] < inset.Pixels[p + 2]) continue;
            land.Add(index);
            int px = index % width, py = index / width;
            if (px > 0) pending.Enqueue(index - 1);
            if (px + 1 < width) pending.Enqueue(index + 1);
            if (py > 0) pending.Enqueue(index - width);
            if (py + 1 < height) pending.Enqueue(index + width);
        }
        if (land.Count == 0) throw new InvalidDataException("Missing opening island artwork.");
        // The blue sea and its frame move with the island. The green mainland
        // fragment and decorative margin outside the inset do not.
        for (int index = 0; index < width * height; index++)
        {
            int p = index * 4;
            if (inset.Pixels[p + 3] != 0 && inset.Pixels[p + 2] > inset.Pixels[p + 1]) land.Add(index);
        }
        // Retain the one-pixel coast/frame outlines and antialiasing.
        var coverage = land.SelectMany(index => Enumerable.Range(Math.Max(0, index / width - 1),
                Math.Min(height - 1, index / width + 1) - Math.Max(0, index / width - 1) + 1)
            .SelectMany(py => Enumerable.Range(Math.Max(0, index % width - 1),
                Math.Min(width - 1, index % width + 1) - Math.Max(0, index % width - 1) + 1).Select(px => py * width + px))).ToHashSet();
        byte[] island = (byte[])inset.Pixels.Clone();
        for (int py = 0; py < height; py++)
        for (int px = 0; px < width; px++)
        {
            int index = py * width + px, p = index * 4;
            if (!coverage.Contains(index)) island[p + 3] = 0;
        }
        // Keep the frame exclusively in the movable inset. Copying it to the base
        // would leave the old island location visibly framed.
        var basis = cityMask is null ? RemovePreviewSeaFrame(background) : background;
        IndexedImageArchive.ExtendTransparentColors(island, width, height);
        return (basis, inset with { Pixels = island });
    }
}
