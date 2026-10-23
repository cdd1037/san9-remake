using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static class OpeningMapCompiler
{
    // Opening-page composition: place the island in the northeastern sea on both maps.
    private const int IslandVerticalOffset = -256;
    public static OpeningMapDefinition Compile(byte[] executable, byte[] archive, TextureLibrary library)
    {
        using var pe = new PEReader(new MemoryStream(executable));
        var (palettes, images) = IndexedImageArchive.ReadArchive(archive);
        var mask = images[166];
        string Asset(int image) => library.Archives.Single(a => a.Id == "g_mmap").Variants.Single(v => v.ImageIndex == image).Image.Id;
        int Int(int rva) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(rva).GetContent(0, 4).AsSpan());
        Dictionary<int, OpeningMapCity> cities = [];
        // RE-0195: source indices identify cities. Publish semantic city areas, not source palette indices.
        for (int id = 0; id < 50; id++)
        {
            List<OpeningMapRun> runs = [];
            for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.Pixels[y * mask.Width + x] != id + 1) continue;
                int start = x;
                while (x + 1 < mask.Width && mask.Pixels[y * mask.Width + x + 1] == id + 1) x++;
                runs.Add(new(start, y, x - start + 1));
            }
            int dy = id == 45 ? IslandVerticalOffset : 0;
            cities.Add(id, new(new(Int(0x26d6b8 + id * 8), Int(0x26d6bc + id * 8) + dy),
                runs.Select(r => r with { Y = r.Y + dy }).ToArray()));
        }
        return new(mask.Width, mask.Height, Asset(168), Asset(167), new(new(Asset(165), 496, 320 + IslandVerticalOffset), 45), cities,
            Enumerable.Range(0, 51).ToDictionary(i => $"color-{i}", i => new OpeningMapColor(Asset(104 + i),
                i < 50 ? library.Archives.Single(a => a.Id == "g_start2").Variants.Single(v => v.ImageIndex == 86 + i).Image.Id : null,
                Convert.ToHexString(palettes[8].AsSpan((i + 1) * 4, 3)))), "color-50");
    }

    public static OpeningMapPreview Preview(byte[] executable, OpeningMapDefinition map, StartMenuSprite background, StartMenuSprite inset)
    {
        var projection = FitPreview(executable, map.Cities.ToDictionary(c => c.Key, c => c.Value.Center), map.Inset.CityId);
        var positionedInset = inset with { Y = inset.Y + (int)Math.Round(IslandVerticalOffset * projection.Y) };
        return new(background, positionedInset, projection.X, projection.Y, projection.OffsetX, projection.OffsetY);
    }

    private static (double X, double Y, double OffsetX, double OffsetY) FitPreview(byte[] executable,
        IReadOnlyDictionary<int, ScenarioPreviewPoint> centers, int insetCity)
    {
        using var pe = new PEReader(new MemoryStream(executable));
        int Int(int rva) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(rva).GetContent(0, 4).AsSpan());
        // Calibrate the page's projection once during import. Only the canonical city anchors are published.
        (double Scale, double Offset) Fit(Func<ScenarioPreviewPoint, int> coordinate, int offset)
        {
            var pairs = centers.Where(c => c.Key != insetCity)
                .Select(c => (X: (double)coordinate(c.Value), Y: (double)Int(0x2689c0 + c.Key * 8 + offset) + 8)).ToArray();
            double x = pairs.Average(p => p.X), y = pairs.Average(p => p.Y);
            double scale = pairs.Sum(p => (p.X - x) * (p.Y - y)) / pairs.Sum(p => (p.X - x) * (p.X - x));
            return (scale, y - scale * x);
        }
        var horizontal = Fit(p => p.X, 0); var vertical = Fit(p => p.Y, 4);
        return (horizontal.Scale, vertical.Scale, horizontal.Offset, vertical.Offset);
    }

    public static IndexedImage AlignPreviewArtwork(byte[] executable, IndexedImage source, bool inset)
    {
        using var pe = new PEReader(new MemoryStream(executable));
        int Int(int rva) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(rva).GetContent(0, 4).AsSpan());
        var centers = Enumerable.Range(0, 50).ToDictionary(id => id,
            id => new ScenarioPreviewPoint(Int(0x26d6b8 + id * 8), Int(0x26d6bc + id * 8)));
        var projection = FitPreview(executable, centers, 45);
        int ox = inset ? 344 : 0, oy = inset ? 214 : 0;
        var anchors = centers.Where(c => (c.Key == 45) == inset).Select(c =>
        {
            double x = c.Value.X * projection.X + projection.OffsetX, y = c.Value.Y * projection.Y + projection.OffsetY;
            return new MapArtworkAnchor((int)Math.Round(x) - ox, (int)Math.Round(y) - oy,
                Int(0x2689c0 + c.Key * 8) + 8 - ox, Int(0x2689c4 + c.Key * 8) + 8 - oy);
        }).Concat(new[] { new MapArtworkAnchor(0, 0, 0, 0), new(source.Width - 1, 0, source.Width - 1, 0),
            new(0, source.Height - 1, 0, source.Height - 1), new(source.Width - 1, source.Height - 1, source.Width - 1, source.Height - 1) }).ToArray();
        return MapArtworkRegistration.Align(source, anchors);
    }
}
