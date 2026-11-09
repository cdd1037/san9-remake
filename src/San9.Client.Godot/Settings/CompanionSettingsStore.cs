using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Application.Persistence;
using San9.Core.Domain;

namespace San9.Client.Godot.Settings;

/// <summary>Independent companion configuration; saving never writes a game save.</summary>
public sealed class CompanionSettingsStore
{
    private const int FormatVersion = 23;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
    };
    private readonly string _path;

    public CompanionSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute filesystem path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public CompanionConfiguration Load()
    {
        string json;
        try { json = File.ReadAllText(_path, Utf8); }
        catch (FileNotFoundException) { return CompanionConfiguration.Default; }
        catch (DirectoryNotFoundException) { return CompanionConfiguration.Default; }
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Companion configuration is null.");
        if (document.Version != FormatVersion) throw new InvalidDataException("Unsupported companion configuration version.");
        var settings = document.Settings ?? throw new InvalidDataException("Companion settings are missing.");
        try { settings.Validate(); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid companion configuration.", error); }
        return new(settings, document.FormationAppearanceEnabled);
    }

    public void Save(CompanionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = configuration.Rules;
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var document = new SettingsDocument { Version = FormatVersion, Settings = settings,
            FormationAppearanceEnabled = configuration.FormationAppearanceEnabled };
        AtomicFile.Write(_path, Utf8.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\n"));
    }

    private sealed class SettingsDocument
    {
        public required int Version { get; init; }
        public required CompanionSettings? Settings { get; init; }
        public required bool FormationAppearanceEnabled { get; init; }
    }
}
