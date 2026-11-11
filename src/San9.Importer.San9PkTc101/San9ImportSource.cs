using San9.Content.Text;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Importer.San9PkTc101.Scenarios;
using San9.Importer.San9PkTc101.Interface;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101;

/// <summary>Shared raw inputs and source-only message compilation for one import operation.</summary>
internal sealed class San9ImportSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScenarioSource> _scenarios = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string File, string Catalog), (Dictionary<string, MessageDefinition> Messages, MessageContentAdaptation[] Adaptations)> _messages = [];
    private readonly Dictionary<string, (ScenarioInitialState Scenario, TextCatalog Text)> _worlds = [];
    private Dictionary<string, string>? _audio;
    internal Dictionary<string, string> Audio => _audio ??= AudioAssetCompiler.Discover(Directory.Root);
    internal San9SourceDirectory Directory { get; }
    internal byte[] Executable => Read("San9PK.exe");

    internal bool OriginalTextEdits { get; }

    private San9ImportSource(string directory, bool originalTextEdits)
    {
        Directory = new(directory);
        OriginalTextEdits = originalTextEdits;
    }

    internal static async Task<San9ImportSource> OpenAsync(string directory, CancellationToken token, bool originalTextEdits = true)
    {
        var source = new San9ImportSource(directory, originalTextEdits);
        byte[] bytes = source.Executable;
        using var stream = new MemoryStream(bytes, writable: false);
        var identity = await San9PkTc101Detector.DetectAsync(stream, bytes.Length, token);
        if (!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        return source;
    }

    internal async Task<TexturePackage> LoadTexturesAsync(string root, CancellationToken token)
    {
        var textures = await TexturePackageFiles.LoadAsync(root, token);
        if (textures.SourceBuild != San9PkTc101Detector.SourceBuildId) throw new InvalidDataException("Texture source build differs.");
        return textures;
    }

    internal byte[] Read(string name)
    {
        if (_files.TryGetValue(name, out var value)) return value;
        byte[] bytes = File.ReadAllBytes(Directory.File(name));
        _files.Add(name, bytes);
        return bytes;
    }

    internal ScenarioSource Scenario(string name)
    {
        if (_scenarios.TryGetValue(name, out var value)) return value;
        var scenario = new ScenarioSource(Read(name));
        _scenarios.Add(name, scenario);
        return scenario;
    }

    internal void ReleaseScenario(string name) { _worlds.Remove(name); _scenarios.Remove(name); }

    internal (ScenarioInitialState Scenario, TextCatalog Text) World(string name, CancellationToken token)
    {
        if (_worlds.TryGetValue(name, out var value)) return value;
        var result = ScenarioContentCompiler.Compile(Executable, Scenario(name), Read("M_MsgPK.s9"), Read("M_RtdnPK.s9"), token, OriginalTextEdits);
        _worlds.Add(name, result);
        return result;
    }

    internal (Dictionary<string, MessageDefinition> Messages, MessageContentAdaptation[] Adaptations) Messages(string file, string catalog, CancellationToken token)
    {
        if (_messages.TryGetValue((file, catalog), out var value)) return value;
        var result = MessageArchiveCompiler.CompileForPublication(Read(file), file, catalog, token: token, originalTextEdits: OriginalTextEdits);
        _messages.Add((file, catalog), result);
        return result;
    }
}
