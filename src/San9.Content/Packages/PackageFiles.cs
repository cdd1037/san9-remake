using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace San9.Content.Packages;

public static class PackageFiles
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Asset path must be relative.");
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset path escapes package.");
        return result;
    }

    public static async Task<T> ReadAsync<T>(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token) ?? throw new InvalidDataException("Empty content file.");
    }
}
