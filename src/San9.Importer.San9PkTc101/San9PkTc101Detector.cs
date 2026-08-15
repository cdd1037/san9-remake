using System.Security.Cryptography;

namespace San9.Importer.San9PkTc101;

public sealed record SourceDetectionResult(
    bool IsMatch,
    long Size,
    string Sha256,
    string Reason);

public static class San9PkTc101Detector
{
    public const long ExpectedSize = 2_636_800;
    public const string ExpectedSha256 = "D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028";
    public const string SourceBuildId = "san9pk-tc-1.0.1.0-d20794ae";

    public static async Task<SourceDetectionResult> DetectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo file = new(path);
        if (!file.Exists)
        {
            return new(false, 0, string.Empty, "Source file does not exist.");
        }

        if (file.Length != ExpectedSize)
        {
            return new(false, file.Length, string.Empty, "Source file size does not match the registered build.");
        }

        await using FileStream stream = file.OpenRead();
        return await DetectAsync(stream, file.Length, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SourceDetectionResult> DetectAsync(
        Stream stream,
        long size,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(stream));
        }

        if (size != ExpectedSize)
        {
            return new(false, size, string.Empty, "Source file size does not match the registered build.");
        }

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        string sha256 = Convert.ToHexString(hash);
        bool isMatch = string.Equals(sha256, ExpectedSha256, StringComparison.Ordinal);
        return new(
            isMatch,
            size,
            sha256,
            isMatch ? "Registered build matched." : "SHA-256 does not match the registered build.");
    }
}
