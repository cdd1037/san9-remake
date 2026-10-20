using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot.Settings;

/// <summary>Explicit synchronous storage; the caller owns the current settings and error presentation.</summary>
public sealed class UserSettingsStore
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<MessageSpeed>(allowIntegerValues: false),
            new JsonStringEnumConverter<ProgressReportMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<PlaybackSpeed>(allowIntegerValues: false) },
    };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _path;

    public UserSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute filesystem path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public UserSettings Load()
    {
        string json;
        try { json = File.ReadAllText(_path, Utf8); }
        catch (FileNotFoundException) { return UserSettings.Default; }
        catch (DirectoryNotFoundException) { return UserSettings.Default; }

        var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("User settings document is null.");
        if (document.Version != FormatVersion)
            throw new InvalidDataException($"Unsupported user settings version: {document.Version}.");
        return document.Settings ?? throw new InvalidDataException("User settings are missing.");
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = new SettingsDocument
        {
            Version = FormatVersion,
            Settings = settings,
        };
        byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\n");
        AtomicUserFile.Write(_path, bytes);
    }

    private sealed class SettingsDocument
    {
        public required int Version { get; init; }
        public required UserSettings? Settings { get; init; }
    }
}
