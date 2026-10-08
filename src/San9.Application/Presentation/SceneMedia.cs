namespace San9.Application.Presentation;

public enum BackgroundMusicAction { Preserve, Stop, Play }
public sealed record BackgroundMusicRequest(BackgroundMusicAction Action, string? Role = null);
public sealed record MovieRequest(string Role);

/// <summary>A scene owns requests; a platform owns playback and cancellation. No source filenames enter this boundary.</summary>
public interface ISceneMedia
{
    void RequestBackgroundMusic(BackgroundMusicRequest request);
    Task PlayMovieAsync(MovieRequest request, CancellationToken cancellationToken);
}

public sealed class SilentSceneMedia : ISceneMedia
{
    public void RequestBackgroundMusic(BackgroundMusicRequest request) { }
    public Task PlayMovieAsync(MovieRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
