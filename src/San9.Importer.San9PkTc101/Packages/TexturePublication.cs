namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Replace each completed file atomically; no old-file comparison, backup or batch rollback.</summary>
internal static class TexturePublication
{
    public static void Write(string target, byte[] bytes)
    {
        string pending = target + ".pending";
        try
        {
            File.WriteAllBytes(pending, bytes);
            File.Move(pending, target, true);
        }
        finally { File.Delete(pending); }
    }
}
