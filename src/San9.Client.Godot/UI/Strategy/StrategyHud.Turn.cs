using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public TurnPresentation Turn { get; private set; } = null!;
    public bool CanIssueOrders => Interaction.CanIssueOrders;
    public bool CanQuickSave => Interaction.CanSave;
    private readonly List<Control> _strategicHeader = [];
    private TextureButton _advanceButton = null!, _informationButton = null!;
    private GameDialog _turnDialog = null!;
    private string _turnConfirmation = "";
    private Label _turnStatus = null!;
    private TextureRect _turnBanner = null!;
    private StrategyButton _pauseTurn = null!;
    private TextureProgressBar _turnProgress = null!;
    private ExecutionPanels _executionPanels = null!;
    private readonly Texture2D[][] _turnTitles = new Texture2D[2][];
    private readonly CanvasItemMaterial _titleFlashMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    private SceneSoundPlayer _sceneSounds = null!;
    private BattlePlaybackFlow _battle = null!;

    private void BuildTurnControls()
    {
        _sceneSounds = new() { ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds };
        AddChild(_sceneSounds);
        _battle = new() { Session = Session, World = World, Turn = Turn, Textures = Textures, Texts = Texts,
            CommandPresentation = CommandPresentation, ContentRoot = ContentRoot, Music = Music, SceneSounds = _sceneSounds,
            SoundEnabled = () => SoundEnabled, NarrationActive = () => _presentation?.HasNarration == true,
            Narrate = (messages, view, completed) => _presentation.OpenNarration(messages, view, completed),
            Record = PresentMapRecord, Speech = PresentMapSpeech, MouseFilter = MouseFilterEnum.Ignore, Size = Size };
        AddChild(_battle);
        _executionPanels = new ExecutionPanels { Textures = _textures, Texts = Texts, Skin = Skin, RecordsLog = RecordsLog };
        RecordsLog.ReportMode = ReadSettings().ProgressReport;
        _executionPanels.LocationRequested += World.FocusCell;
        AddChild(_executionPanels);
        BuildMapMessages();
        _turnStatus = LabelAt("", new(270, 320), new(484, 96), 24, parent: _center);
        for (int phase = 0; phase < 2; phase++)
            _turnTitles[phase] = Enumerable.Range(0, 4).Select(frame => (Texture2D)_textures.Get($"turn-title-{(phase == 0 ? "execution" : "strategy")}-{frame}", this)).ToArray();
        _turnBanner = new TextureRect { Name = "TurnTitle", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_turnBanner);
        _turnProgress = new TextureProgressBar
        {
            Position = new(285, 19),
            Size = new(304, 16),
            MaxValue = 304,
            Step = 1,
            TextureUnder = _textures.Get("turn-progress-frame", this),
            TextureProgress = _textures.Get("turn-progress-fill", this),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _topLeft.AddChild(_turnProgress);
        _pauseTurn = (StrategyButton)AddButton("静止", "button-pause", new(602, 10), new(64, 36), true, () => Turn.TogglePause());
        _pauseTurn.ToggleMode = true;
        _turnDialog = new GameDialog { Textures = _textures, ZIndex = 50, Visible = false };
        _turnDialog.Confirmed += () => Turn.Confirm();
        _turnDialog.Dismissed += () => Turn.Cancel();
        AddChild(_turnDialog);
        _turnDialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _seasonChangeView = new() { Textures = _textures, RetryRequested = World.RetrySeasonResources };
        AddChild(_seasonChangeView);
    }

    private void RequestAdvance()
    {
        if (!Interaction.CanAdvance) return;
        if (!Turn.Request()) return;
        _turnConfirmation = Session.View.PlayerForceId is int force ? Session.Messages(Texts).Produce("messages.6082",
            new Dictionary<string, MessageBinding>
            {
                ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, force)
            }, _ => throw new InvalidDataException("Turn confirmation must not require randomness.")).BbCode : "继续电脑势力的行动？";
        Overview.Close();
        _mapContext.Close(); _commandMenu.Close();
        Selection.Clear(); World.HoverObject(null);
        _hoverCard.ShowInfo(null, Vector2.Zero);
        UpdateTurnControls();
    }

    private void UpdateTurn(double delta)
    {
        var previous = Turn.Screen;
        AdvanceSeasonChange(delta);
        Turn.PlaybackSpeed = ReadSettings().PlaybackSpeed;
        Turn.Update(delta);
        if (previous == TurnScreen.Returning && Turn.Screen == TurnScreen.Strategy) _feedback.ResetRecords();
        _presentation.Update();
        World.Presentation = Turn.Display;
        World.Day = Turn.Day;
        UpdateSeasonPresentation();
        _battle.Update();
        _feedback.Advance(delta);
        bool showPanels = Turn.Screen is TurnScreen.Preparing or TurnScreen.Playing or TurnScreen.AwaitingDecision or TurnScreen.Presenting ||
            Turn.Screen == TurnScreen.Failed && _executionPanels.Expanded;
        _executionPanels.Advance(showPanels || _feedback.RecordsRequested, delta, _presentation?.HasDecision == true);
        _executionPanels.Bind(Turn.Display ?? Session.View);
        UpdateTurnControls();
    }

    private void FocusIncomingPlayer()
    {
        var view = Session.View;
        if (view.PlayerForceId is not int force ||
            view.World.Simulation.Controls.Count(c => c.Kind == San9.Core.Simulation.ControllerKind.Player) < 2) return;
        var rulerId = view.Forces.Single(f => f.Id == force).LeaderId;
        var ruler = view.Officers.SingleOrDefault(o => o.Id == rulerId);
        if (ruler?.ServicePlaceId is int place && view.Settlements.Any(s => s.Id == place && !s.IsDestroyed))
            World.FocusBuilding(place);
        else if (ruler?.UnitId is int unit)
            World.FocusUnit(unit);
    }

    private void UpdateTurnControls()
    {
        foreach (var control in _strategicHeader) control.Visible = Turn.IsStrategy && Session.View.PlayerForceId is not null && !SelectingTargets;
        _year.Visible = _date.Visible = _season.Visible = !SelectingTargets;
        _advanceButton.Disabled = !Interaction.CanAdvance;
        _informationButton.Disabled = !Interaction.CanSave;
        _functionsButton.Disabled = !Interaction.CanSave;
        _minimap!.MouseFilter = Turn.CanBrowseMap ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _turnDialog.Visible = Turn.Screen is TurnScreen.Confirmation or TurnScreen.Failed;
        if (Turn.Screen == TurnScreen.Failed)
            _turnDialog.SetMessage($"推进失败：{Turn.Error}", "重试", Turn.CanCancelFailure ? "返回" : null);
        else if (Turn.Screen == TurnScreen.Confirmation)
            _turnDialog.SetRichMessage(_turnConfirmation);
        _turnStatus.Text = Turn.Screen switch
        {
            TurnScreen.Preparing => "势力决策中",
            TurnScreen.AwaitingDecision => "等待玩家决策",
            TurnScreen.Terminal => "本局结束",
            _ => "",
        };
        _turnStatus.Visible = _turnStatus.Text.Length != 0 && _presentation?.HasDecision != true && _presentation?.HasEnding != true;
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
        _turnProgress.Visible = _pauseTurn.Visible = Turn.IsExecutionPresentation;
        _turnProgress.Value = Math.Floor(304 * Turn.Progress);
        _pauseTurn.SetPressedNoSignal(Turn.Paused);
        // The calendar remains at the starting xun until the committed return boundary.
        var clock = Turn.SeasonChangeOrigin is { } previousDate && _seasonChange?.ShowsNewBadge != true
            ? previousDate : Turn.Display?.World.Clock ?? Session.Snapshot.Clock;
        if (Turn.Screen is not (TurnScreen.Entering or TurnScreen.Playing))
        { _year.Text = $"{clock.Year}年"; _date.Text = $"{clock.Month}月{(clock.Day <= 10 ? "上旬" : clock.Day <= 20 ? "中旬" : "下旬")}"; }
    }
}
