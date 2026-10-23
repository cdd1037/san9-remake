namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Restores fixed-path images and their manifest if an in-place conversion fails.</summary>
internal sealed class TexturePublication : IDisposable
{
    private readonly Dictionary<string, string?> _backups = new(StringComparer.OrdinalIgnoreCase);
    private bool _committed;

    public void Write(string target, byte[] bytes)
    {
        if (!_backups.ContainsKey(target))
        {
            string? backup = File.Exists(target) ? target + ".previous" : null;
            if (backup is not null) File.Copy(target, backup, true);
            _backups.Add(target, backup);
        }
        string pending = target + ".pending";
        try
        {
            File.WriteAllBytes(pending, bytes);
            File.Move(pending, target, true);
        }
        finally { File.Delete(pending); }
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        foreach (var (target, backup) in _backups.Reverse())
        {
            if (!_committed)
            {
                if (backup is null) File.Delete(target);
                else File.Move(backup, target, true);
            }
            else if (backup is not null) File.Delete(backup);
        }
        _backups.Clear();
    }
}
