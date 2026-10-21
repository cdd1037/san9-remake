namespace San9.Application.Persistence;

public static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> bytes) => Write(path, bytes, null);

    internal static void Write(string path, ReadOnlySpan<byte> bytes, Action? beforeReplace)
    {
        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            beforeReplace?.Invoke();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
