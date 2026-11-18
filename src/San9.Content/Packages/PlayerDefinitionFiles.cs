namespace San9.Content.Packages;

/// <summary>One complete player-owned document beside its base definition; no field merging.</summary>
public static class PlayerDefinitionFiles
{
    public static string PlayerPath(string definitionPath) =>
        definitionPath.EndsWith(".player.json", StringComparison.OrdinalIgnoreCase) ? definitionPath : Path.ChangeExtension(definitionPath, ".player.json");

    public static string ActivePath(string definitionPath)
    {
        string player = PlayerPath(definitionPath);
        try { _ = File.GetAttributes(player); return player; }
        catch (FileNotFoundException) { return definitionPath; }
        catch (DirectoryNotFoundException) { return definitionPath; }
    }

    public static async Task<byte[]?> ReadOptionalAsync(string path, CancellationToken token = default)
    {
        try { return await File.ReadAllBytesAsync(path, token); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
