using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>Edits a private draft; cancel and failed writes leave the active preferences intact.</summary>
public partial class EnvironmentSettingsView : SettingsView
{
    public UserSettings Initial { get; init; } = null!;
    public Action<UserSettings> Apply { get; init; } = null!;
    private UserSettings _draft = null!;
    protected override string Title => "设定环境";
    protected override string[] Labels => ["讯息速度", "进行记录", "BGM", "音效"];
    protected override int LabelWidth => 128;
    protected override int ColumnCount => 3;
    protected override int LabelFontSize => 14;
    protected override int ButtonFontSize => 22;

    protected override void BuildOptions()
    {
        _draft = Initial;
        string[] speeds = ["普通", "快", "暂停"];
        MessageSpeed[] speedValues = [MessageSpeed.Normal, MessageSpeed.Fast, MessageSpeed.Pause];
        string[] reports = ["详细", "要约", "OFF"];
        ProgressReportMode[] reportValues = [ProgressReportMode.Detailed, ProgressReportMode.Summary, ProgressReportMode.Off];
        for (int i = 0; i < 3; i++)
        {
            var speed = speedValues[i]; var report = reportValues[i];
            Option(speeds[i], i, 0, () => _draft = _draft with { MessageSpeed = speed }, () => _draft.MessageSpeed == speed);
            Option(reports[i], i, 1, () => _draft = _draft with { ProgressReport = report }, () => _draft.ProgressReport == report);
        }
        for (int i = 0; i < 2; i++)
        {
            bool enabled = i == 0;
            Option(enabled ? "ON" : "OFF", i, 2, () => _draft = _draft with { MusicEnabled = enabled }, () => _draft.MusicEnabled == enabled);
            Option(enabled ? "ON" : "OFF", i, 3, () => _draft = _draft with { SoundEnabled = enabled }, () => _draft.SoundEnabled == enabled);
        }
    }
    protected override void Confirm()
    {
        try { Apply(_draft); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowFailure($"环境设定保存失败：\n{error.Message}");
            return;
        }
        Closed();
    }
}
