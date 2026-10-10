using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private CommandNarration? _narration;
    private readonly Queue<(IReadOnlyList<CommandMessage> Messages, StrategySnapshot View)> _pendingNarrations = new();

    private void OpenNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view)
    {
        if (messages.Count == 0) return;
        if (_narration is not null || _decision is not null)
        { _pendingNarrations.Enqueue((messages,view)); return; }
        _textures.RequestPrefixes("narration-", "dialog-");
        _narration = new() { Messages = messages, View = view, Texts = Texts, Service = Textures, Textures = _textures,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled,
            BeginMusic = _sceneMusic.Begin,
            MessagePresented = m => _executionPanels.Append(m.Message), Closed = CloseNarration };
        OverlayHost.AddChild(_narration);
    }

    private void CloseNarration()
    {
        if (_narration is not { } view) return;
        _narration = null;
        OverlayHost.RemoveChild(view);
        view.QueueFree();
        ContinueNarrations();
    }
    private void ContinueNarrations()
    {
        if (_narration is null && _decision is null && _pendingNarrations.TryDequeue(out var next))
            OpenNarration(next.Messages,next.View);
    }
}
