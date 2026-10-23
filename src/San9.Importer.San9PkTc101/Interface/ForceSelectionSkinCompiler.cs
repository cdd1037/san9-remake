using San9.Content.Images;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>The opening picker has its own blue/gold chrome, independent of in-game information windows.</summary>
public static class ForceSelectionSkinCompiler
{
    public static Dictionary<string, ImageAsset> Compile(TextureLibrary library)
    {
        ImageAsset Image(string archive, int index) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == index).Image;
        Dictionary<string, ImageAsset> skin = [];
        foreach (var (role, image) in new[] { ("background", 161), ("frame-bottom-left", 166), ("frame-top-left", 167),
            ("frame-bottom-right", 168), ("frame-top-right", 169), ("bottom", 162), ("left", 163), ("right", 164), ("top", 165),
            ("title-left", 184), ("title-middle", 185), ("title-right", 186),
            ("footer-left", 208), ("footer-middle", 209), ("footer-right", 210) })
            skin.Add("opening-" + role, Image("g_start2", image));
        skin.Add("opening-panel", Image("g_mmap", 169));
        foreach (var (role, index) in new[] { ("catalog-header-left", 202), ("catalog-header-middle", 203), ("catalog-header-right", 204) })
        for (int frame = 0; frame < 4; frame++)
        {
            var source = Image("g_start2", index);
            string key = role + (frame == 0 ? "" : $"-{frame}");
            skin.Add(key, source with { Id = key, X = source.X + frame * source.Width / 4, Width = source.Width / 4 });
        }
        for (int frame = 0; frame < 6; frame++)
        {
            var source = Image("g_start2", 207);
            string key = "catalog-tab" + (frame == 0 ? "" : $"-{frame}");
            skin.Add(key, source with { Id = key, X = source.X + frame * source.Width / 6, Width = source.Width / 6 });
        }
        foreach (var (prefix, first) in new[] { ("information-action-", 176), ("catalog-accept-", 181),
            ("information-action-map-", 170), ("catalog-bulk-", 173) })
        for (int part = 0; part < 3; part++)
        for (int frame = 0; frame < 4; frame++)
        {
            string role = prefix + new[] { "left", "middle", "right" }[part] + (frame == 0 ? "" : $"-{frame}");
            var source = Image("g_start2", first + part);
            int width = source.Width / 4;
            skin.Add(role, source with { Id = role, X = source.X + frame * width, Width = width });
        }
        foreach (var (role, index) in new[] { ("information-action-close", 179), ("catalog-help", 180) })
        for (int frame = 0; frame < 4; frame++)
        {
            string key = role + (frame == 0 ? "" : $"-{frame}");
            var source = Image("g_start2", index);
            int width = source.Width / 4;
            skin.Add(key, source with { Id = key, X = source.X + frame * width, Width = width });
        }
        return skin;
    }
}
