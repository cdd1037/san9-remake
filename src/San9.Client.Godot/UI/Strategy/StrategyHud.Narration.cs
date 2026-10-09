using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private CommandNarration? _narration;

    private void OpenNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view)
    {
        _textures.RequestPrefixes("narration-", "dialog-");
        _narration = new() { Messages = messages, View = view, Texts = Texts, Service = Textures, Textures = _textures,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled,
            MessagePresented = m => _executionPanels.Append(m.Message), Closed = CloseNarration };
        OverlayHost.AddChild(_narration);
    }

    private void CloseNarration()
    {
        if (_narration is not { } view) return;
        _narration = null;
        OverlayHost.RemoveChild(view);
        view.QueueFree();
    }
}
