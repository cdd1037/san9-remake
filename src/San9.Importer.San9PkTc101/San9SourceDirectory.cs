namespace San9.Importer.San9PkTc101;

/// <summary>All original inputs belong to one selected game installation directory.</summary>
public sealed class San9SourceDirectory
{
    public string Root { get; }
    public string Executable { get; }

    public San9SourceDirectory(string directory)
    {
        Root = Path.GetFullPath(directory);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Game directory not found: {Root}");
        Executable = File("San9PK.exe");
    }

    public string File(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            Path.IsPathRooted(name) || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("Source file must be a file name inside the selected game directory.", nameof(name));
        string path = Path.Combine(Root, name);
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException($"Missing game file: {name}", path);
        return path;
    }
}
