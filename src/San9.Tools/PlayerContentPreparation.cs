using San9.Application.Diagnostics;
using San9.Importer.San9PkTc101.Packages;

internal static class PlayerContentPreparation
{
    internal static async Task RunAsync(string gameDirectory, string output, string? ffmpeg, bool music, bool effects, bool movies, bool originalTextEdits)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await PlayerContentImporter.ImportAsync(gameDirectory, output,
                token => ContentVerification.VerifyAsync(output, Console.WriteLine, token),
                ffmpeg, music, effects, movies, update => Console.WriteLine(update.Message), cancellation.Token, originalTextEdits);
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
