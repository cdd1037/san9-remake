using San9.Application;
using San9.Client.Godot.Settings;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Edits local presentation and recorded world rules in one dialog.</summary>
public partial class PkSettingsView : SettingsView
{
    private enum Setting { TacticScenes, DuelScenes, DiplomacyScenes, OtherOrders, OtherTactics, BattleDeaths }
    private static readonly (string Label, Setting? Value)[] Rows =
    [
        ("兵法演出", Setting.TacticScenes), ("单挑演出", Setting.DuelScenes),
        ("外交演出", Setting.DiplomacyScenes),
        ("他势力部队的命令", Setting.OtherOrders), ("他势力部队的兵法", Setting.OtherTactics),
        ("武将的阵亡", Setting.BattleDeaths)
    ];
    public SimulationState Initial { get; init; } = null!;
    public UserSettings InitialSettings { get; init; } = null!;
    public Action<UserSettings> ApplySettings { get; init; } = null!;
    public Func<PkSettingsOperation, SessionResult> ApplyWorld { get; init; } = null!;
    protected override string Title => "PK设定";
    protected override string[] Labels => Rows.Select(row => row.Label).ToArray();
    protected override int LabelWidth => 176;
    protected override int ColumnCount => 2;
    private PkSettingsOperation _worldDraft = null!;
    private PkSettingsOperation _initialWorld = null!;
    private UserSettings _settingsDraft = null!;
    private UserSettings _savedSettings = null!;
    protected override void BuildOptions()
    {
        _worldDraft = _initialWorld = new(Initial.OtherForceOrdersVisible, Initial.OtherForceTacticsVisible, Initial.BattleDeathsEnabled);
        _settingsDraft = _savedSettings = InitialSettings;
        for (int row = 0; row < Rows.Length; row++)
        {
            var setting = Rows[row].Value;
            bool deaths = setting == Setting.BattleDeaths;
            Option(deaths ? "有" : "显示", 0, row, setting is { } on ? () => Select(on, true) : null,
                () => setting is null || Selected(setting.Value));
            Option(deaths ? "无" : "不显示", 1, row, setting is { } off ? () => Select(off, false) : null,
                () => setting is not null && !Selected(setting.Value));
        }
    }

    private bool Selected(Setting setting) => setting switch
    {
        Setting.DuelScenes => _settingsDraft.DuelScenesEnabled,
        Setting.TacticScenes => _settingsDraft.TacticScenesEnabled,
        Setting.DiplomacyScenes => _settingsDraft.DiplomacyScenesEnabled,
        Setting.OtherOrders => _worldDraft.OtherForceOrdersVisible,
        Setting.OtherTactics => _worldDraft.OtherForceTacticsVisible,
        Setting.BattleDeaths => _worldDraft.BattleDeathsEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };
    private void Select(Setting setting, bool value)
    {
        if (setting == Setting.TacticScenes) _settingsDraft = _settingsDraft with { TacticScenesEnabled = value };
        else if (setting == Setting.DuelScenes) _settingsDraft = _settingsDraft with { DuelScenesEnabled = value };
        else if (setting == Setting.DiplomacyScenes) _settingsDraft = _settingsDraft with { DiplomacyScenesEnabled = value };
        else _worldDraft = setting switch
        {
            Setting.OtherOrders => _worldDraft with { OtherForceOrdersVisible = value },
            Setting.OtherTactics => _worldDraft with { OtherForceTacticsVisible = value },
            Setting.BattleDeaths => _worldDraft with { BattleDeathsEnabled = value },
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
    }
    protected override void Confirm()
    {
        bool settingsChanged = _settingsDraft != _savedSettings;
        if (settingsChanged)
        {
            try { ApplySettings(_settingsDraft); _savedSettings = _settingsDraft; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                ShowFailure($"PK设定保存失败：\n{error.Message}");
                return;
            }
        }
        if (_worldDraft == _initialWorld) { Closed(); return; }
        var result = ApplyWorld(_worldDraft);
        if (result.Disposition == SessionDisposition.Accepted) { Closed(); return; }
        ShowFailure($"战局设定未能执行：{result.Rejection}" +
            (_savedSettings != InitialSettings ? "\n演出偏好已保存。" : ""));
    }
}
