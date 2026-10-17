namespace San9.Client.Godot.Settings;

public enum MessageSpeed { Normal, Fast, Pause }
public enum ProgressReportMode { Detailed, Summary, Off }
public enum PlaybackSpeed { Normal = 1, Double = 2, Quadruple = 4 }

/// <summary>Local presentation preferences, independent of the simulation and game saves.</summary>
public sealed record UserSettings(
    bool MusicEnabled,
    bool SoundEnabled,
    bool MoviesEnabled,
    MessageSpeed MessageSpeed,
    ProgressReportMode ProgressReport)
{
    public bool TacticScenesEnabled { get; init; } = true;
    public PlaybackSpeed PlaybackSpeed { get; init; } = PlaybackSpeed.Normal;

    public static UserSettings Default { get; } = new(true, true, true,
        MessageSpeed.Normal, ProgressReportMode.Detailed);
}
