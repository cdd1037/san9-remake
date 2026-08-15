using San9.Importer.San9PkTc101;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9PkTc101DetectorTests
{
    [Fact]
    public async Task DetectAsync_RejectsUnexpectedSizeWithoutGuessing()
    {
        await using MemoryStream stream = new([0x4D, 0x5A]);

        SourceDetectionResult result = await San9PkTc101Detector.DetectAsync(stream, stream.Length);

        Assert.False(result.IsMatch);
        Assert.Empty(result.Sha256);
        Assert.Contains("size", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_RejectsSameSizedContentWithWrongHash()
    {
        await using MemoryStream stream = new(new byte[San9PkTc101Detector.ExpectedSize]);

        SourceDetectionResult result = await San9PkTc101Detector.DetectAsync(stream, stream.Length);

        Assert.False(result.IsMatch);
        Assert.Equal(San9PkTc101Detector.ExpectedSize, result.Size);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Contains("SHA-256", result.Reason, StringComparison.Ordinal);
    }
}
