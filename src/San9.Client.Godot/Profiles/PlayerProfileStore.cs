using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Application.Profiles;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot.Profiles;

/// <summary>Explicit storage; the caller owns the current profile and retries.</summary>
public sealed class PlayerProfileStore
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _path;

    public PlayerProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute filesystem path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public PlayerProfile Load()
    {
        string json;
        try { json = File.ReadAllText(_path, Utf8); }
        catch (FileNotFoundException) { return PlayerProfile.Empty; }
        catch (DirectoryNotFoundException) { return PlayerProfile.Empty; }

        var document = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Player profile document is null.");
        if (document.Version != FormatVersion)
            throw new InvalidDataException($"Unsupported player profile version: {document.Version}.");
        if (document.CompletedMilestones is null)
            throw new InvalidDataException("Completed milestones are missing.");
        try { return new PlayerProfile(document.CompletedMilestones); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid milestone identifier.", error); }
    }

    public void Save(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var document = new ProfileDocument
        {
            Version = FormatVersion,
            CompletedMilestones = profile.CompletedMilestones.Order(StringComparer.Ordinal).ToArray(),
        };
        byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\n");
        AtomicUserFile.Write(_path, bytes);
    }

    private sealed class ProfileDocument
    {
        public required int Version { get; init; }
        public required string[]? CompletedMilestones { get; init; }
    }
}
