using System.Diagnostics;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Authoring converts source music to engine-independent PCM WAV; clients never decode source WMA.</summary>
internal static class AudioTranscoder
{
    internal static async Task<byte[]> ToWaveAsync(byte[] source, CancellationToken token)
    {
        string input = Path.GetTempFileName(), output = input + ".wav";
        try
        {
            await File.WriteAllBytesAsync(input, source, token);
            var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-v", "error", "-y", "-i", input, "-vn", "-c:a", "pcm_s16le", "-ar", "44100", output })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start FFmpeg for music conversion.");
            using var cancellation = token.Register(() => { if (!process.HasExited) process.Kill(entireProcessTree: true); });
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException("Music conversion failed: " + error);
            return await File.ReadAllBytesAsync(output, token);
        }
        finally { File.Delete(input); File.Delete(output); }
    }
}
