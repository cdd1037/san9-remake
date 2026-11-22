using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>The optional export-only content preparation adapter; game loading still consumes converted packages.</summary>
internal static class ContentImportService
{
    internal static bool Available =>
#if SAN9_EXPORT_IMPORTER
        true;
#else
        false;
#endif

    internal static string? FindEncoder()
    {
#if SAN9_EXPORT_IMPORTER
        return San9.Importer.San9PkTc101.Packages.PlayerContentImporter.FindEncoder();
#else
        return null;
#endif
    }

    internal static Task ImportAsync(string source, string output, string? ffmpeg, bool music, bool effects, bool movies, bool originalTextEdits,
        Action<ContentImportProgress> progress, CancellationToken token)
    {
#if SAN9_EXPORT_IMPORTER
        return Task.Run(async () =>
        {
            await San9.Importer.San9PkTc101.Packages.PlayerContentImporter.ImportAsync(source, output,
                cancellation => San9.Application.Diagnostics.ContentVerification.VerifyAsync(output,
                    message => progress(new(ContentImportStage.Verifying, message)), cancellation),
                ffmpeg, music, effects, movies, progress, token, originalTextEdits);
        }, token);
#else
        throw new InvalidOperationException("开发构建不包含内置转换器，请使用 San9.Tools。");
#endif
    }
}
