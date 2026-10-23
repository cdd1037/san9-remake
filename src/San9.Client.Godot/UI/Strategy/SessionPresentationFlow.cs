using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Owns ordered execution results, narration, decisions and the terminal sequence.</summary>
public partial class SessionPresentationFlow : Node
{
    public GameSession Session { get; init; } = null!;
    public TurnPresentation Turn { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public TextureScope Scope { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public CommandPresentation CommandPresentation { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public StrategyOverlayHost OverlayHost { get; init; } = null!;
    public SceneMusicPlayer Music { get; init; } = null!;
    public ISceneMedia SceneMedia { get; init; } = null!;
    public Func<Settings.UserSettings> ReadSettings { get; init; } = null!;
    private bool SoundEnabled => ReadSettings().SoundEnabled;
    public bool IntroduceNewGame { get; init; }
    private readonly HashSet<int> _introducedForces = [];
    public Action? StartNewGame { get; init; }
    public Action LoadGame { get; init; } = null!;
    public Func<MapMessageRecord, bool> PresentMapRecord { get; init; } = null!;
    public Func<MapSpeechMessage, bool> PresentMapSpeech { get; init; } = null!;
    public Func<IReadOnlyList<CommandMessage>, IReadOnlyList<CommandMessage>> PresentExecutionMessages { get; init; } = null!;
    public Action<MapCell> FocusRecord { get; init; } = null!;
    public Func<string, bool> IsSpeechComplete { get; init; } = null!;
    public Action<string> DismissSpeech { get; init; } = null!;
    public bool HasNarration => _narration is not null;
    public bool HasDecision => _decision is not null;
    public bool HasEnding => _endingView is not null;
    public bool HasQueuedNarration => _pendingNarrations.Count != 0;
    public long Publication => _executionPublication;
    private (int? Force, San9.Core.Simulation.GameClock? Clock)? _contextPresented;
    private long _strategyEntrancePresented;
    public Action FocusIncomingPlayer { get; init; } = null!;
    public override void _Ready() => _contextPresented = (Session.View.PlayerForceId, Session.StrategicContext.Clock);
    private GameDialog? _failure;
    public void Update()
    {
        if (_failure is not null) return;
        try
        {
            UpdateExecutionMessages();
            if (_failure is not null) return;
            UpdateDecision();
            if (_failure is not null) return;
            UpdateEnding(); UpdateStrategyEntrance();
        }
        catch (Exception error)
        {
            if (_decision is { } decision) RemoveWindow(decision);
            if (_endingView is { } ending) RemoveWindow(ending);
            ShowFailure(error, () => { _decisionRevision = -1; _terminalPresented = false; });
        }
    }
    private void ShowFailure(Exception error, Action? retry = null)
    {
        if (_failure is not null) return;
        var failure = new GameDialog { Textures = Scope };
        _failure = failure;
        failure.SetMessage($"无法显示事件：{error.Message}", "重试", "结束");
        failure.Confirmed += () => { RemoveWindow(failure); retry?.Invoke(); ContinueNarrations(); };
        failure.Dismissed += () => GetTree().Quit();
        ShowWindow(failure, () => _failure = null, InteractionBlock.Orders | InteractionBlock.Continuation | InteractionBlock.Playback);
        failure.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
    private void UpdateStrategyEntrance()
    {
        if (Turn.Screen == TurnScreen.Strategy && Turn.StrategyEntrance != _strategyEntrancePresented &&
            Session.Snapshot.Simulation.Boundary == San9.Core.Simulation.SimulationBoundary.Stable)
        {
            _strategyEntrancePresented = Turn.StrategyEntrance;
            FocusIncomingPlayer();
            if (CommandPresentation.Progress is { } progress)
            {
                var context = Session.Messages(Texts);
                List<CommandMessage> sequence = [];
                if (IntroduceNewGame && Session.View.PlayerForceId is int force && !_introducedForces.Contains(force))
                {
                    sequence.AddRange(NewGameIntroduction.Produce(Session.View, context, Texts, progress));
                    _introducedForces.Add(force);
                }
                sequence.AddRange(StrategyEntryPresentation.Produce(Session.View, context, Texts, progress));
                if (Session.StrategicContext.Clock is { } clock && _contextPresented != (Session.View.PlayerForceId, clock))
                {
                    _contextPresented = (Session.View.PlayerForceId, clock);
                    sequence.AddRange(StrategicContext.Produce(Session.StrategicContext, Session.View, context, Texts, progress));
                }
                OpenNarration(sequence, Session.View);
            }
        }
    }
    private WorkflowWindows? _windows;
    private void ShowWindow(Control view, Action? released = null, InteractionBlock blocked = InteractionBlock.Orders) =>
        (_windows ??= new(OverlayHost)).Show(view, released, blocked);
    private void RemoveWindow(Control view) => _windows!.Close(view);
    public override void _ExitTree()
    {
        _pendingNarrations.Clear();
        _windows?.Dispose();
        _diplomacyNarrated.Clear();
    }

    private DecisionView? _decision;
    private long _decisionPublication;
    private long _decisionRevision = -1;
    private readonly HashSet<Guid> _diplomacyNarrated = [];
    private EndingView? _endingView;
    private bool _terminalPresented;
    private long _endingPublication;
    private long _executionPublication;
    private void UpdateExecutionMessages()
    {
        if (Turn.Publication > _executionPublication && Turn.Feedback is { } feedback)
        {
            var before = feedback.Before;
            var output = ExecutionPublication.Read(before, feedback.After, Session.Messages(Texts, before.World), Texts,
                CommandPresentation, feedback.Events, _diplomacyNarrated, Turn.LastPresentedFeedback,
                startIndex: feedback.StartIndex, endIndex: feedback.EndIndex);
            foreach (var record in output.Records) PresentMapRecord(record);
            OpenNarration(PresentExecutionMessages(output.Messages),before);
            _executionPublication = Turn.Publication;
        }
    }

    private void UpdateEnding()
    {
        if (_endingView is { Pending: true } ending && Turn.CommittedPublication > _endingPublication)
        { _endingPublication = Turn.CommittedPublication; ending.Complete(); }
        if (Turn.Screen != TurnScreen.Terminal || _terminalPresented || _narration is not null) return;
        _terminalPresented = true;
        if (Session.GetEndingPrompt() is null) { ShowSessionEnd(); return; }
        Scope.RequestPrefixes("decision-", "catalog-", "information-", "narration-", "dialog-", "progress-record-", "detail-", "catalog-scroll-", "message-", "text-icon-");
        _endingPublication = Turn.CommittedPublication;
        _endingView = new()
        {
            Draft = new(Session, Texts, CommandPresentation.Decisions),
            Textures = Scope,
            Service = Textures,
            Texts = Texts,
            Presentation = CommandPresentation,
            ContentRoot = ContentRoot,
            SoundEnabled = SoundEnabled,
            ReadMessageSpeed = () => ReadSettings().MessageSpeed,
            Media = SceneMedia,
            BeginMusic = Music.Begin,
            Chronicle = closed => new InformationJournalView
            {
                Textures = Scope, Skin = Skin, Closed = closed,
                Read = () => ChroniclePresentation.Read(Session, Texts, CommandPresentation.Chronicle!, Session.GetTerminalResult().WinningForceId),
                Hint = role => ChroniclePresentation.Hint(Session, Texts, CommandPresentation.Chronicle!, role, Session.GetTerminalResult().WinningForceId)
            },
            Submit = Turn.SelectEnding,
            Finished = () =>
            {
                var view = _endingView!; _endingView = null; RemoveWindow(view); ShowSessionEnd();
            }
        };
        ShowWindow(_endingView, () => _endingView = null);
    }

    private void ShowSessionEnd()
    {
        Scope.RequestPrefixes("decision-", "dialog-");
        ShowWindow(new ReturnToTitleView { Definition = CommandPresentation.Ending!, Textures = Scope, Service = Textures,
            NewGame = StartNewGame, LoadGame = LoadGame });
    }

    private void UpdateDecision()
    {
        if (_decision is { Pending: true } active && Turn.CommittedPublication > _decisionPublication && Turn.LastResult is { } result)
        {
            _decisionPublication = Turn.CommittedPublication;
            var continuation = active.Draft.Meeting is not null ? DecisionDraft.Read(Session, Texts, CommandPresentation) : null;
            active.Complete(result, continuation);
        }
        if (_decision is not null || _narration is not null || Turn.Screen != TurnScreen.AwaitingDecision || Turn.IsComputing) return;
        if (_decisionRevision == Session.Snapshot.Revision) return;
        Scope.RequestPrefixes("decision-", "deployment-", "detail-", "catalog-", "information-", "narration-", "dialog-");
        try
        {
            var draft = DecisionDraft.Read(Session, Texts, CommandPresentation);
            if (draft is null) return;
            _decisionPublication = Turn.CommittedPublication;
            _decisionRevision = Session.Snapshot.Revision;
            _decision = new()
            {
                Draft = draft,
                Textures = Scope,
                Service = Textures,
                Texts = Texts,
                Presentation = CommandPresentation,
                ContentRoot = ContentRoot,
                SoundEnabled = SoundEnabled,
                ReadMessageSpeed = () => ReadSettings().MessageSpeed,
                DiplomacyScenesEnabled = ReadSettings().DiplomacyScenesEnabled,
                Submit = Turn.Respond,
                Finished = CloseDecision,
                Media = SceneMedia,
                SkipIntroduction = draft.Diplomacy is { } diplomacy && !_diplomacyNarrated.Add(diplomacy.TaskId),
                BeginMusic = Music.Begin
            };
            ShowWindow(_decision, () => _decision = null, InteractionBlock.Orders | InteractionBlock.Continuation);
        }
        catch (Exception error)
        {
            if (_decision is { } incomplete) { _decision = null; RemoveWindow(incomplete); }
            _decisionRevision = Session.Snapshot.Revision;
            ShowFailure(error, () => _decisionRevision = -1);
        }
    }
    private void CloseDecision()
    {
        if (_decision is not { } view) return;
        _decision = null; RemoveWindow(view);
        ContinueNarrations();
    }

    private CommandNarration? _narration;
    private readonly Queue<(IReadOnlyList<CommandMessage> Messages, StrategySnapshot View, Action? Completed, string? Sound)> _pendingNarrations = new();

    public void OpenNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view, Action? completed = null, string? initialSound = null)
    {
        messages = DiplomacyNarration.Filter(messages, ReadSettings().DiplomacyScenesEnabled);
        if (messages.Count == 0) { completed?.Invoke(); ContinueNarrations(); return; }
        _pendingNarrations.Enqueue((messages, view, completed, initialSound));
        ContinueNarrations();
    }
    private void BeginNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view, string? initialSound)
    {
        Scope.RequestPrefixes("narration-", "dialog-");
        _narration = new() { Messages = messages, View = view, Texts = Texts, Service = Textures, Textures = Scope, ZIndex = 4,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, InitialSoundRole = initialSound, ReadMessageSpeed = () => ReadSettings().MessageSpeed,
            BeginMusic = Music.Begin,
            DiplomacyPresentation = CommandPresentation.Diplomacy,
            Media = SceneMedia, MoviesEnabled = () => ReadSettings().MoviesEnabled,
            MessagePresented = message =>
            {
                if (message.FocusLocation is { } location)
                {
                    FocusRecord(location);
                }
                if (message.WaitForMapSpeech && message.MapSpeech is { } speech) PresentMapSpeech(speech);
            },
            IsMapSpeechComplete = speech => IsSpeechComplete(speech.Id),
            DismissMapSpeech = speech => DismissSpeech(speech.Id),
            Closed = CloseNarration };
        ShowWindow(_narration, () => _narration = null, InteractionBlock.Orders | InteractionBlock.Continuation | InteractionBlock.Playback);
    }

    private void CloseNarration()
    {
        if (_narration is not { } view) return;
        _narration = null;
        RemoveWindow(view);
        var completed = _pendingNarrations.Dequeue().Completed;
        try { completed?.Invoke(); }
        finally { ContinueNarrations(); }
    }
    private void ContinueNarrations()
    {
        if (_narration is not null || _decision is not null || _failure is not null || !_pendingNarrations.TryPeek(out var next)) return;
        try { BeginNarration(next.Messages, next.View, next.Sound); }
        catch (Exception error)
        {
            if (_narration is { } incomplete) RemoveWindow(incomplete);
            ShowFailure(error);
        }
    }
}
