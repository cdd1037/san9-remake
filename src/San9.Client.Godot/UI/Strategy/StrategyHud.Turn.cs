using Godot;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public TurnPresentation Turn { get; private set; } = null!;
    public bool CanIssueOrders => Turn is { CanAdvance: true } && _deployment is null;
    private readonly List<Control> _strategicHeader = [];
    private TextureButton _advanceButton = null!, _informationButton = null!;
    private GameDialog _turnDialog = null!;
    private Label _turnStatus = null!;
    private TextureRect _turnBanner = null!;
    private StrategyButton _pauseTurn = null!;
    private TextureProgressBar _turnProgress = null!;
    private readonly Texture2D[][] _turnTitles = new Texture2D[2][];
    private readonly CanvasItemMaterial _titleFlashMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    private void BuildTurnControls()
    {
        _turnStatus = LabelAt("", new(270, 320), new(484, 96), 24);
        for (int phase = 0; phase < 2; phase++)
            _turnTitles[phase] = Enumerable.Range(0, 4).Select(frame => (Texture2D)_textures[$"turn-title-{(phase == 0 ? "execution" : "strategy")}-{frame}"]).ToArray();
        _turnBanner = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_turnBanner);
        _turnProgress = new TextureProgressBar { Position = new(285, 19), Size = new(304, 16), MaxValue = 304, Step = 1,
            TextureUnder = _textures["turn-progress-frame"], TextureProgress = _textures["turn-progress-fill"],
            MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_turnProgress);
        _pauseTurn = (StrategyButton)AddButton("静止", "button-pause", new(602, 10), new(64, 36), true, () => Turn.TogglePause());
        _pauseTurn.ToggleMode = true;
        _turnDialog = new GameDialog { Textures = _textures, ZIndex = 50, Visible = false };
        _turnDialog.Confirmed += () => Turn.Confirm();
        _turnDialog.Dismissed += () => Turn.Cancel();
        AddChild(_turnDialog);
        _turnDialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void RequestAdvance()
    {
        if (_deployment is not null) return;
        if (!Turn.Request()) return;
        Overview.Close();
        _contextMenu.Hide(); _mapContext.Close(); _commandMenu.Close();
        Selection.Clear(); World.HoverObject(null);
        _hoverCard.ShowInfo(null, Vector2.Zero);
        UpdateTurnControls();
    }

    private void UpdateTurn(double delta)
    {
        Turn.Update(delta);
        World.Presentation = Turn.Display;
        UpdateTurnControls();
    }

    private void UpdateTurnControls()
    {
        foreach (var control in _strategicHeader) control.Visible = Turn.IsStrategy && _targets is null;
        _year.Visible = _date.Visible = _season.Visible = _targets is null;
        _advanceButton.Disabled = !Turn.CanAdvance;
        _informationButton.Disabled = !CanIssueOrders;
        _minimap!.MouseFilter = Turn.CanBrowseMap ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _turnDialog.Visible = Turn.Screen is TurnScreen.Confirmation or TurnScreen.Failed;
        if (_turnDialog.Visible)
            _turnDialog.SetMessage(Turn.Screen == TurnScreen.Failed ? $"推进失败：{Turn.Error}" :
                $"将结束{Texts.Resolve(Scenario.Forces.Single(f => f.Id == Scenario.PlayerForceId).NameKey)}军的战略面，\n您确定吗？",
                Turn.Screen == TurnScreen.Failed ? "重试" : "是", Turn.Screen == TurnScreen.Failed ? (Turn.CanCancelFailure ? "返回" : null) : "否");
        _turnStatus.Text = Turn.Screen switch
        {
            TurnScreen.Preparing => "势力决策中",
            TurnScreen.AwaitingDecision => "等待玩家决策",
            TurnScreen.Terminal => "本局结束",
            _ => "",
        };
        _turnStatus.Visible = _turnStatus.Text.Length != 0;
        _turnBanner.Visible = Turn.Screen is TurnScreen.Entering or TurnScreen.Returning;
        if (_turnBanner.Visible)
        {
            int frame = Turn.TitleFrame;
            _turnBanner.Texture = _turnTitles[Turn.Screen == TurnScreen.Entering ? 0 : 1][frame];
            _turnBanner.Size = _turnBanner.Texture.GetSize();
            _turnBanner.Position = (Size - _turnBanner.Texture.GetSize()) / 2;
            _turnBanner.Material = frame < 3 ? _titleFlashMaterial : null;
            _turnBanner.SelfModulate = new Color(1, 1, 1, frame < 3 ? 128f / 256 : 1);
        }
        _turnProgress.Visible = _pauseTurn.Visible = Turn.Screen == TurnScreen.Playing;
        _turnProgress.Value = Math.Floor(304 * Turn.Progress);
        _pauseTurn.SetPressedNoSignal(Turn.Paused);
        // The calendar remains at the starting xun until the committed return boundary.
        var clock = Turn.Display?.World.Clock ?? Session.Snapshot.Clock;
        if (Turn.Screen is not (TurnScreen.Entering or TurnScreen.Playing))
        { _year.Text = $"{clock.Year}年"; _date.Text = $"{clock.Month}月{(clock.Day <= 10 ? "上旬" : clock.Day <= 20 ? "中旬" : "下旬")}"; }
    }
}
