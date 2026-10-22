using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private CommandNarration? _narration;
    private readonly Queue<(IReadOnlyList<CommandMessage> Messages, StrategySnapshot View, Action? Completed)> _pendingNarrations = new();

    private void OpenNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view, Action? completed = null)
    {
        messages = DiplomacyNarration.Filter(messages, ReadSettings().DiplomacyScenesEnabled);
        if (messages.Count == 0) { completed?.Invoke(); ContinueNarrations(); return; }
        if (_narration is not null || _decision is not null)
        { _pendingNarrations.Enqueue((messages,view,completed)); return; }
        _textures.RequestPrefixes("narration-", "dialog-");
        _narration = new() { Messages = messages, View = view, Texts = Texts, Service = Textures, Textures = _textures, ZIndex = 4,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, ReadMessageSpeed = () => ReadSettings().MessageSpeed,
            BeginMusic = Music.Begin,
            DiplomacyPresentation = CommandPresentation.Diplomacy,
            Media = SceneMedia, MoviesEnabled = () => ReadSettings().MoviesEnabled,
            MessagePresented = message =>
            {
                if (message.FocusLocation is { } location)
                {
                    World.FocusCell(location); _minimap!.PresentRecordLocation(location);
                    _recordSounds.Play(CommandPresentation.MapAudio!.RecordNotificationSound,SoundEnabled);
                }
                if (message.WaitForMapSpeech && message.MapSpeech is { } speech) PresentMapSpeech(speech);
            },
            IsMapSpeechComplete = speech => _mapSpeech.IsComplete(speech.Id),
            DismissMapSpeech = speech => _mapSpeech.Dismiss(speech.Id),
            Closed = () => CloseNarration(completed) };
        OverlayHost.AddWorkflow(_narration, InteractionBlock.Orders | InteractionBlock.Continuation | InteractionBlock.Playback);
    }

    private void CloseNarration(Action? completed = null)
    {
        if (_narration is not { } view) return;
        _narration = null;
        OverlayHost.RemoveChild(view);
        view.QueueFree();
        completed?.Invoke();
        ContinueNarrations();
    }
    private void ContinueNarrations()
    {
        if (_narration is null && _decision is null && _pendingNarrations.TryDequeue(out var next))
            OpenNarration(next.Messages,next.View,next.Completed);
    }
}
