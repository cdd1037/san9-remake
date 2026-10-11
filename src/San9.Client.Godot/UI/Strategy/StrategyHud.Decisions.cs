using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private DecisionView? _decision;
    private long _decisionPublication;
    private long _decisionRevision = -1;
    private readonly HashSet<Guid> _diplomacyNarrated = [];
    private EndingView? _endingView;
    private bool _terminalPresented;
    private long _endingPublication;
    private StrategySnapshot? _executionBefore;
    private long _executionPublication;
    private void UpdateExecutionMessages()
    {
        if (Turn.Publication > _executionPublication && Turn.LastResult is { } result && _executionBefore is { } before)
        {
            _executionPublication = Turn.Publication;
            var output = ExecutionPublication.Read(before, Session.View, Session.Messages(Texts, before.World), Texts, CommandPresentation, result.Events, _diplomacyNarrated, Turn.LastPresentedFacts);
            foreach (var record in output.Records) PresentMapRecord(record);
            OpenNarration(PresentExecutionMessages(output.Messages),before);
        }
        _executionBefore = Session.View;
    }

    private void UpdateEnding()
    {
        if (_endingView is { Pending: true } ending && Turn.Publication > _endingPublication)
        { _endingPublication = Turn.Publication; ending.Complete(); }
        if (Turn.Screen != TurnScreen.Terminal || _terminalPresented || _narration is not null) return;
        _terminalPresented = true;
        if (Session.GetEndingPrompt() is null) { ShowSessionEnd(); return; }
        _textures.RequestPrefixes("decision-", "catalog-", "information-", "narration-", "dialog-");
        _endingPublication = Turn.Publication;
        _endingView = new()
        {
            Draft = new(Session, Texts, CommandPresentation.Decisions),
            Textures = _textures,
            Service = Textures,
            Texts = Texts,
            Presentation = CommandPresentation,
            ContentRoot = ContentRoot,
            SoundEnabled = SoundEnabled,
            ReadMessageSpeed = () => ReadSettings().MessageSpeed,
            Media = SceneMedia,
            Submit = Turn.SelectEnding,
            Finished = () =>
            {
                var view = _endingView!; _endingView = null; OverlayHost.RemoveChild(view); view.QueueFree(); ShowSessionEnd();
            }
        };
        OverlayHost.AddChild(_endingView);
    }

    private void ShowSessionEnd()
    {
        var dialog = new GameDialog { Textures = _textures };
        dialog.SetMessage("本局结束。", StartNewGame is null ? "结束" : "新游戏", "结束");
        dialog.Confirmed += () => { if (StartNewGame is null) GetTree().Quit(); else StartNewGame(); };
        dialog.Dismissed += () => GetTree().Quit(); OverlayHost.AddChild(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void UpdateDecision()
    {
        if (_decision is { Pending: true } active && Turn.Publication > _decisionPublication && Turn.LastResult is { } result)
        {
            _decisionPublication = Turn.Publication;
            active.Complete(result);
        }
        if (_decision is not null || _narration is not null || Turn.Screen != TurnScreen.AwaitingDecision || Turn.IsComputing) return;
        if (_decisionRevision == Session.Snapshot.Revision) return;
        _textures.RequestPrefixes("decision-", "deployment-", "detail-", "catalog-", "information-", "narration-", "dialog-");
        try
        {
            var draft = DecisionDraft.Read(Session, Texts, CommandPresentation);
            if (draft is null) return;
            _decisionPublication = Turn.Publication;
            _decisionRevision = Session.Snapshot.Revision;
            _decision = new()
            {
                Draft = draft,
                Textures = _textures,
                Service = Textures,
                Texts = Texts,
                Presentation = CommandPresentation,
                ContentRoot = ContentRoot,
                SoundEnabled = SoundEnabled,
                ReadMessageSpeed = () => ReadSettings().MessageSpeed,
                Submit = Turn.Respond,
                Finished = CloseDecision,
                Media = SceneMedia,
                SkipIntroduction = draft.Diplomacy is { } diplomacy && !_diplomacyNarrated.Add(diplomacy.TaskId),
                BeginMusic = Music.Begin
            };
            OverlayHost.AddChild(_decision);
        }
        catch (Exception error)
        {
            _decisionRevision = Session.Snapshot.Revision;
            var failure = new GameDialog { Textures = _textures };
            failure.SetMessage($"无法显示待答事件：{error.Message}", "重试", "结束");
            failure.Confirmed += () => { OverlayHost.RemoveChild(failure); failure.QueueFree(); _decisionRevision = -1; };
            failure.Dismissed += () => GetTree().Quit();
            OverlayHost.AddChild(failure); failure.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
    }
    private void CloseDecision()
    {
        if (_decision is not { } view) return;
        _decision = null; OverlayHost.RemoveChild(view); view.QueueFree();
        ContinueNarrations();
    }
}
