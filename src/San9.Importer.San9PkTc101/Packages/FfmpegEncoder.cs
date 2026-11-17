using System.ComponentModel;
using System.Diagnostics;

namespace San9.Importer.San9PkTc101.Packages;

internal interface IMediaEncoder
{
    Task EncodeAsync(string source, string output, IReadOnlyList<string> options, CancellationToken token);
}

internal sealed class FfmpegEncoder(string executable) : IMediaEncoder
{
    internal string ExecutablePath => Path.GetFullPath(executable);

    internal static readonly string[] AudioOptions = ["-map", "0:a:0", "-vn", "-af", "asetpts=N/SR/TB",
        "-c:a", "libvorbis", "-q:a", "5", "-ar", "44100", "-ac", "2", "-map_metadata", "-1"];
    internal static readonly string[] MovieOptions = ["-map", "0:v:0", "-map", "0:a:0", "-vf",
        "deblock=filter=weak:block=8:alpha=0.05:beta=0.025:gamma=0.025:delta=0.025,hqdn3d=1.0:0.75:1.5:1.0,unsharp=5:5:0.25:5:5:0,tpad=stop=-1:stop_mode=clone",
        "-c:v", "libtheora", "-q:v", "4", "-g:v", "64", "-pix_fmt", "yuv420p",
        "-af", "asetpts=N/SR/TB", "-c:a", "libvorbis", "-q:a", "5", "-ar", "44100", "-ac", "2",
        "-shortest", "-map_metadata", "-1"];

    internal static FfmpegEncoder? Find() => Find(Environment.GetEnvironmentVariable("PATH") ?? "");

    internal static FfmpegEncoder? Find(string searchPath)
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (string directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(directory.Trim().Trim('"'), name);
            if (File.Exists(path)) return new(path);
        }
        return null;
    }

    public async Task EncodeAsync(string source, string output, IReadOnlyList<string> options, CancellationToken token)
    {
        // Source decoding can report defects in the original WMV3/MP3 files. The
        // published output must independently pass a strict complete decode.
        await RunAsync(["-nostdin", "-hide_banner", "-loglevel", "warning", "-y", "-i", source,
            .. options, output], token);
        await RunAsync(["-nostdin", "-hide_banner", "-v", "error", "-xerror", "-i", output,
            "-f", "null", "-"], token, rejectDiagnostics: true);
    }

    private async Task RunAsync(string[] arguments, CancellationToken token, bool rejectDiagnostics = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception error) { throw new IOException("Could not start the ffmpeg executable found on PATH.", error); }
        using var cancellation = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stderr = process.StandardError.ReadToEndAsync(token);
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { await process.WaitForExitAsync(CancellationToken.None); throw; }
        string diagnostics = await stderr;
        await stdout;
        if (process.ExitCode != 0 || (rejectDiagnostics && !string.IsNullOrWhiteSpace(diagnostics)))
            throw new InvalidDataException($"ffmpeg failed ({process.ExitCode}): {diagnostics}");
    }
}
