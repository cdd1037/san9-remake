using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public TurnPresentation Turn { get; private set; } = null!;
    public bool CanIssueOrders => Interaction.CanIssueOrders && !SidebarMoving && _patrol is null && _facilityCommand is null && _demolition is null && _appointment is null && _personnel is null && _mission is null && _corpsCommand is null && _narration is null;
    public bool CanQuickSave => _built && Turn.Screen == TurnScreen.Strategy && CanIssueOrders &&
        !_flow.IsActive && !_notice.Visible && !OverlayHost.HasModal;
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

    private San9.Core.Simulation.GameClock? _contextPresented;
    private long _strategyEntrancePresented;
    private SceneSoundPlayer _sceneSounds = null!;
    private MapSoundPlayer _mapSounds = null!;
    private readonly MapAudioPlayback _mapAudioPlayback = new(Random.Shared.Next);
    private BattlePresentationView _battleView = null!;
    private TurnDayPresentation? _battleMessagesDay;
    private readonly HashSet<string> _battleMessagesPlayed = [];

    private void BuildTurnControls()
    {
        _contextPresented = Session.StrategicContext.Clock;
        _sceneSounds = new() { ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds };
        AddChild(_sceneSounds);
        _mapSounds = new() { ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds };
        AddChild(_mapSounds);
        BuildMapMessages();
        _battleView = new()
        {
            Textures = Textures,
            Texts = Texts,
            Size = Size,
            ZIndex = 3,
            MouseFilter = MouseFilterEnum.Ignore,
            ContentRoot = ContentRoot,
            Sounds = CommandPresentation.Sounds,
            SoundEnabled = () => SoundEnabled,
            PauseRequested = () => Turn.TogglePause(),
            PlaySound = role => _sceneSounds.Play(role, SoundEnabled),
            PlayMapSound = (role, point) => _mapSounds.PlayOnce(role, point, SoundEnabled),
            IsPaused = () => Turn.Paused,
            FocusRequested = obj => World.FocusCell(BattleCell(obj)),
            Project = obj =>
            {
                Vector2 point;
                if (!obj.IsSettlement && World.TrySpeechUnitAnchor(obj.Id, out var anchor, out _)) point = anchor;
                else
                {
                    var cell = BattleCell(obj); var center = MapGeometry.Center(cell.X, cell.Y);
                    point = new(center.X, center.Y - 10 * (World.Package!.Squares[cell.Y * World.Package.Width + cell.X].Elevation - 1));
                }
                return GetGlobalTransformWithCanvas().AffineInverse() * (World.GetGlobalTransformWithCanvas() * point);
            }
        };
        AddChild(_battleView);
        _executionPanels = new ExecutionPanels { Textures = _textures, Texts = Texts, Skin = Skin, RecordsLog = _mapRecords };
        _mapRecords.ReportMode = ReadSettings().ProgressReport;
        _executionPanels.LocationRequested += World.FocusCell;
        AddChild(_executionPanels);
        _turnStatus = LabelAt("", new(270, 320), new(484, 96), 24);
        for (int phase = 0; phase < 2; phase++)
            _turnTitles[phase] = Enumerable.Range(0, 4).Select(frame => (Texture2D)_textures[$"turn-title-{(phase == 0 ? "execution" : "strategy")}-{frame}"]).ToArray();
        _turnBanner = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_turnBanner);
        _turnProgress = new TextureProgressBar
        {
            Position = new(285, 19),
            Size = new(304, 16),
            MaxValue = 304,
            Step = 1,
            TextureUnder = _textures["turn-progress-frame"],
            TextureProgress = _textures["turn-progress-fill"],
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
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
        if (_flow.IsActive || _patrol is not null || _facilityCommand is not null || _demolition is not null || _appointment is not null || _personnel is not null || _mission is not null || _corpsCommand is not null || _narration is not null) return;
        if (!Turn.Request()) return;
        _turnConfirmation = Session.Messages(Texts).Produce("messages.6082",
            new Dictionary<string, MessageBinding>
            {
                ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, Session.View.PlayerForceId)
            }, _ => throw new InvalidDataException("Turn confirmation must not require randomness.")).BbCode;
        Overview.Close();
        _contextMenu.Hide(); _mapContext.Close(); _commandMenu.Close();
        Selection.Clear(); World.HoverObject(null);
        _hoverCard.ShowInfo(null, Vector2.Zero);
        UpdateTurnControls();
    }

    private void UpdateTurn(double delta)
    {
        var previous = Turn.Screen;
        Turn.Update(delta);
        if (previous == TurnScreen.Returning && Turn.Screen == TurnScreen.Strategy) _mapRecordsRequested = false;
        UpdateExecutionMessages();
        UpdateDecision();
        UpdateEnding();
        if (Turn.Screen == TurnScreen.Strategy && Turn.StrategyEntrance != _strategyEntrancePresented &&
            Session.Snapshot.Simulation.Boundary == San9.Core.Simulation.SimulationBoundary.Stable && CommandPresentation.Progress is { } progress)
        {
            _strategyEntrancePresented = Turn.StrategyEntrance;
            var context = Session.Messages(Texts);
            List<CommandMessage> sequence = [.. StrategyEntryPresentation.Produce(Session.View, context, Texts, progress)];
            if (Session.StrategicContext.Clock is { } clock && _contextPresented != clock)
            {
                _contextPresented = clock;
                sequence.AddRange(StrategicContext.Produce(Session.StrategicContext, Session.View, context, Texts, progress));
            }
            OpenNarration(sequence, Session.View);
        }
        World.Presentation = Turn.Display;
        World.Day = Turn.Day;
        if (_narration is null) _battleView.UpdatePlayback(Turn.Day);
        _mapSounds.TransitionGain = _battleView.MapAudioGain;
        _sceneSounds.SetTransitionGain(_battleView.SceneAudioGain);
        Music.SetMapTransitionGain(_battleView.MapAudioGain);
        UpdateMapAudio();
        UpdateMapMessages(delta);
        bool showPanels = Turn.Screen is TurnScreen.Preparing or TurnScreen.Playing or TurnScreen.AwaitingDecision or TurnScreen.Presenting ||
            Turn.Screen == TurnScreen.Failed && _executionPanels.Expanded;
        _executionPanels.Advance(showPanels || _mapRecordsRequested, delta, _decision is not null);
        _executionPanels.Bind(Turn.Display ?? Session.View);
        UpdateTurnControls();
    }

    private San9.Core.Domain.MapCell BattleCell(San9.Core.Simulation.BattleObjectContext obj)
    {
        if (obj.Position is { } position) return San9.Core.Domain.MapCoordinates.Cell(position);
        var place = (Turn.Day?.Before ?? Session.View).Settlements.First(s => s.Id == obj.ServicePlaceId);
        return new(place.X, place.Y);
    }

    private void PresentBattleDay(TurnDayPresentation day)
    {
        if (!ReferenceEquals(_battleMessagesDay, day)) { _battleMessagesDay = day; _battleMessagesPlayed.Clear(); }
        if (day.Battle is not { } plan) return;
        var dialogues = day.PendingDialogues.Where(c => !_battleMessagesPlayed.Contains(c.Id)).ToArray();
        if (dialogues.Length > 0)
        {
            OpenNarration(dialogues.Select(c => c.Message).ToArray(), day.Before,
                () => { foreach (var cue in dialogues) day.CompleteDialogue(cue.Id); });
            foreach (var cue in dialogues) _battleMessagesPlayed.Add(cue.Id);
        }
        foreach (var cue in day.ReadyMapSpeeches)
        {
            if (!_battleMessagesPlayed.Add(cue.Id)) continue;
            var message = cue.Message;
            if (message.MapSpeech is { } speech) PresentMapSpeech(speech);
            if (message.SoundRole is { } sound) _sceneSounds.Play(sound, SoundEnabled);
        }
        foreach (var cue in plan.Records.Where(c => c.Time <= day.Elapsed))
        {
            if (!_battleMessagesPlayed.Add(cue.Record.Id)) continue;
            PresentMapRecord(cue.Record);
            if (cue.SoundRole is { } sound) _sceneSounds.Play(sound, SoundEnabled);
        }
    }

    private void UpdateMapAudio()
    {
        var audio = CommandPresentation.MapAudio!;
        Music.SetMapTrack(Turn.Screen == TurnScreen.Terminal ? null : MapMusic.Select(Turn.Display ?? Session.View, audio));
        var frame = World.Display?.Presented;
        if (!SoundEnabled || Turn.Screen != TurnScreen.Playing || _narration is not null || World.LoadError is not null || _battleView.IsZoomActive)
        {
            if (!SoundEnabled || Turn.Screen != TurnScreen.Playing || World.LoadError is not null) _mapSounds.StopOneShots();
            _mapSounds.UpdateVoices([], Turn.Day);
            _mapAudioPlayback.Clear();
            return;
        }
        // A pending frame does not end the audible scene. Keep loop positions across daily publication.
        if (Turn.Day is null || frame is null || !World.IsDayReady || !ReferenceEquals(frame.Region.Scene.Day, Turn.Day)) return;
        var transform = GetGlobalTransformWithCanvas().AffineInverse() * World.GetGlobalTransformWithCanvas();
        var sources = frame.Region.Scene.SoundSources(audio, frame.Region.Units).Select(source =>
        {
            var p = transform * new Vector2((float)source.X, (float)source.Y);
            return source with { X = p.X, Y = p.Y, Resource = CommandPresentation.Sounds[source.Role] };
        });
        _mapSounds.UpdateVoices(_mapAudioPlayback.Update(sources, Size.X, Size.Y, audio.BattleAmbience,
            frame.Region.Scene.PlaybackMilliseconds, Turn.Day.Elapsed * 1000 < MapMovementCadence.ArrivalMilliseconds), Turn.Day);
    }

    private void UpdateTurnControls()
    {
        foreach (var control in _strategicHeader) control.Visible = Turn.IsStrategy && !SelectingTargets;
        _year.Visible = _date.Visible = _season.Visible = !SelectingTargets;
        _advanceButton.Disabled = !Turn.CanAdvance;
        _informationButton.Disabled = !CanIssueOrders;
        _functionsButton.Disabled = !CanIssueOrders;
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
        _turnStatus.Visible = _turnStatus.Text.Length != 0 && _decision is null && _endingView is null;
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
