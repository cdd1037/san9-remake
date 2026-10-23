using System.Text.Json;
using San9.Content.Text;

namespace San9.Content.Packages;

public sealed record SharedMessageLibrary(string Path, IReadOnlyDictionary<string, MessageDefinition> Messages);

public sealed record MessageLibraryManifest(int SchemaVersion, string[] Files)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>An explicitly authored shared library. Ownership is by key, never inferred from equal bodies.</summary>
public static class MessageLibraryFiles
{
    public static async Task<Dictionary<string, MessageDefinition>> LoadAsync(string path, CancellationToken token = default)
    {
        var manifest = await PackageFiles.ReadAsync<MessageLibraryManifest>(path, token);
        if (manifest.SchemaVersion != MessageLibraryManifest.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported message library version.");
        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Dictionary<string, MessageDefinition> messages = new(StringComparer.Ordinal);
        foreach (string relative in manifest.Files)
        {
            await using var stream = File.OpenRead(PackageFiles.Resolve(root, relative));
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, MessageDefinition>>(stream, ScenarioContentFiles.DataJsonOptions, token)
                ?? throw new InvalidDataException("Empty message file.");
            ContentStructure.Validate(values);
            foreach (var (key, value) in values)
                if (!messages.TryAdd(key, value)) throw new InvalidDataException($"Duplicate shared message key: {key}.");
        }
        // References may be completed by a scenario's private messages; validate after composition.
        return messages;
    }

    public static async Task<SharedMessageLibrary> WriteAsync(string resourceRoot, string path,
        IReadOnlyDictionary<string, MessageDefinition> messages, IReadOnlyDictionary<string, string>? groups = null,
        CancellationToken token = default)
    {
        MessageGraph.Validate(messages);
        foreach (string key in messages.Keys)
            if (!MessageGraph.ReferencesValid(key, messages)) throw new InvalidDataException($"Invalid or cyclic shared message reference: {key}.");
        string manifestPath = PackageFiles.Resolve(resourceRoot, path);
        string root = System.IO.Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(root);
        string[] previous = File.Exists(manifestPath)
            ? (await PackageFiles.ReadAsync<MessageLibraryManifest>(manifestPath, token)).Files : [];
        var files = messages.GroupBy(pair => ScenarioContentFiles.ValidateCategory(groups?.GetValueOrDefault(pair.Key)
                ?? (pair.Key.Contains('.') ? pair.Key[..pair.Key.IndexOf('.')] : "general")), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key + ".json", group => group.ToDictionary(p => p.Key, p => p.Value));
        if (files.Keys.Any(name => PackageFiles.Resolve(root, name).Equals(manifestPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Message group collides with the library manifest.");
        List<(string Pending, string Target)> staged = [];
        async Task Stage<T>(string target, T value, JsonSerializerOptions options)
        {
            string pending = target + ".pending";
            staged.Add((pending, target));
            await using var stream = File.Create(pending);
            await JsonSerializer.SerializeAsync(stream, value, options, token);
        }
        try
        {
            foreach (var (name, values) in files)
                await Stage(PackageFiles.Resolve(root, name), values, ScenarioContentFiles.MessageJsonOptions);
            await Stage(manifestPath, new MessageLibraryManifest(MessageLibraryManifest.CurrentSchemaVersion, files.Keys.ToArray()), PackageFiles.JsonOptions);
            token.ThrowIfCancellationRequested();
            ScenarioContentFiles.Publish(staged);
            foreach (string obsolete in previous.Except(files.Keys, StringComparer.Ordinal))
                File.Delete(PackageFiles.Resolve(root, obsolete));
        }
        finally { foreach (var file in staged) File.Delete(file.Pending); }
        return new(path, new Dictionary<string, MessageDefinition>(messages, StringComparer.Ordinal));
    }
}
