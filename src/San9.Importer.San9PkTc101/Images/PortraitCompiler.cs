using San9.Content.Images;

namespace San9.Importer.San9PkTc101.Images;

public static class PortraitCompiler
{
    public static string ImageId(int id) => $"portrait/{id}";

    public static PortraitDefinition[] Compile(string dataDirectory, Func<string, byte[]> read,
        Func<string, string, TextureSourceImage, ImageAsset> save)
    {
        var palette = IndexedImageArchive.ReadPalettes(read(Path.Combine(dataDirectory, "P_Face.s9")), 2)[0];
        List<PortraitDefinition> portraits = [];
        foreach (var (file, anchors, count, first) in new[] {
            ("G_FaceL.s9", "D_FacPnt.s9", 786, 0), ("G_FacLPK.s9", "D_FcPtPK.s9", 50, 786) })
        {
            var images = IndexedImageArchive.ReadImages(read(Path.Combine(dataDirectory, file)), count);
            var offsets = read(Path.Combine(dataDirectory, anchors));
            if (offsets.Length != count * 2) throw new InvalidDataException("Invalid portrait anchor table.");
            for (int i = 0; i < count; i++)
            {
                if (images[i].Width != 240 || images[i].Height != 240) throw new InvalidDataException("Invalid source portrait dimensions.");
                int id = first + i;
                var image = save(ImageId(id), $"portraits/{id}.png", TextureSourceImage.Indexed(images[i], palette, false, false));
                var bust = new PortraitAnchor(offsets[i * 2] / 240d, offsets[i * 2 + 1] / 240d);
                var portrait = new PortraitDefinition(id, image, bust, PortraitGeometry.FaceFromBust(bust));
                portrait.Validate(); portraits.Add(portrait);
            }
        }
        return portraits.ToArray();
    }
}
