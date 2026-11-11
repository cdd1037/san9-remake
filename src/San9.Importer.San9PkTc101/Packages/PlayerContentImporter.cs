using San9.Content.Packages;
using System.Diagnostics;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Complete player import, independent of any client or console host.</summary>
public static class PlayerContentImporter
{
    public static async Task ImportAsync(string gameDirectory, string output, Func<CancellationToken, Task> validate,
        string? ffmpeg = null, bool audio = true, bool movies = true,
        Action<ContentImportProgress>? progress = null, CancellationToken token = default, bool originalTextEdits = true)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ContentImportStage stage = ContentImportStage.Checking;
        void Log(string message) => progress?.Invoke(new(stage, message));
        void Stage(ContentImportStage next, string message) { stage = next; Log(message); }
        var source = await San9ImportSource.OpenAsync(gameDirectory, token, originalTextEdits);
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        string sourceRoot = Path.TrimEndingDirectorySeparator(source.Directory.Root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Within(output, sourceRoot) || Within(sourceRoot, output))
            throw new ArgumentException("原版目录与内容目录必须相互独立，不能相同或互相包含。");

        bool Within(string path, string root) => path.Equals(root, comparison) ||
            path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison);

        Log("检查原版版本：仅支持繁体中文三国志 IX 威力加强版 1.0.1.0 的已登记构建。");
        string? encoder = null;
        if (audio || movies)
        {
            encoder = ResolveEncoder(ffmpeg);
            await CheckEncoderAsync(encoder, movies, token);
        }
        token.ThrowIfCancellationRequested();
        using var operation = ImportOperation.Acquire(output);

        Stage(ContentImportStage.Textures, "转换纹理与界面资源……");
        await TexturePackageImporter.ImportCoreAsync(source, Path.Combine(output, "shared", "textures"), Log, token, parent: operation);
        Stage(ContentImportStage.Audio, "准备音乐与音效……");
        if (audio)
        {
            await AudioLibraryImporter.ImportCoreAsync(operation, source.Directory.Root, output, AudioLibraryGroup.All, Log, token, ffmpegPath: encoder);
        }
        else Log("本次不转换音频，保留已有音乐与音效。");
        Stage(ContentImportStage.Scenarios, "转换全部剧本……");
        await ContentCatalogImporter.ImportCoreAsync(operation, source, output, Log, token);
        Stage(ContentImportStage.Movies, "准备影片……");
        if (movies)
        {
            if (!await MovieLibraryImporter.ImportCoreAsync(operation, source.Directory.Root, output, MovieLibraryGroup.All, Log, token, ffmpegPath: encoder))
                throw new InvalidOperationException("找不到 FFmpeg，影片转换未完成。");
        }
        else Log("本次不转换影片，保留已有影片。");
        token.ThrowIfCancellationRequested();
        Stage(ContentImportStage.Verifying, "校验转换内容……");
        await validate(token);
        token.ThrowIfCancellationRequested();
        Stage(ContentImportStage.Complete, "转换与内容校验完成。");
    }

    private static string ResolveEncoder(string? selected)
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        if (!string.IsNullOrWhiteSpace(selected))
        {
            string path = Path.GetFullPath(selected);
            if (!File.Exists(path) || !Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException($"请选择 {name} 可执行文件。", path);
            return path;
        }
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(directory.Trim().Trim('"'), name);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("未找到 FFmpeg。请先安装 FFmpeg，或在界面中选择 ffmpeg 可执行文件后重试。");
    }

    private static async Task CheckEncoderAsync(string executable, bool movies, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-nostdin", "-hide_banner", "-encoders" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 FFmpeg。");
        using var registration = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        string encoders = await stdout;
        string diagnostics = await stderr;
        if (process.ExitCode != 0 || !encoders.Contains("libvorbis", StringComparison.Ordinal) || (movies && !encoders.Contains("libtheora", StringComparison.Ordinal)))
            throw new InvalidDataException("FFmpeg 必须支持 libvorbis 编码器，转换影片还需要 libtheora。" + diagnostics);
    }
}
