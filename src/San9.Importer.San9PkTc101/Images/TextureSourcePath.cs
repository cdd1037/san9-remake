namespace San9.Importer.San9PkTc101.Images;

// Import-time identity, independent of pixels and consumer roles. Derived views are explicit.
public static class TextureSourcePath
{
    public static string Image(string archive, int image, int palette, bool transparent, bool alpha,
        string? paletteArchive = null, int columns = 1, int frame = 0, string? transform = null)
    {
        string mode = !transparent ? "opaque" : alpha ? "alpha" : "key";
        string view = columns == 1 ? "full" : $"columns-{columns}/frame-{frame}";
        return $"original/{archive.ToLowerInvariant()}/{image}/{(paletteArchive ?? archive).ToLowerInvariant()}/{palette}/{mode}/{view}/{transform ?? "source"}.png";
    }
}
