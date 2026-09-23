namespace San9.Client.Godot.Settings;

public enum MessageSpeed { Normal, Fast, Pause }
public enum ProgressReportMode { Detailed, Summary, Off }

/// <summary>Local presentation preferences, independent of the simulation and game saves.</summary>
public sealed record UserSettings(
    bool MusicEnabled,
    bool SoundEnabled,
    bool MoviesEnabled,
    MessageSpeed MessageSpeed,
    ProgressReportMode ProgressReport)
{
    public static UserSettings Default { get; } = new(true, true, true,
        MessageSpeed.Normal, ProgressReportMode.Detailed);
}
