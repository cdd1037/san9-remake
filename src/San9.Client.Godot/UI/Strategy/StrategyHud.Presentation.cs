using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private SessionPresentationFlow _presentation = null!;
    private void BuildSessionPresentation()
    {
        _presentation = new()
        {
            Session = Session, Turn = Turn, Texts = Texts, Textures = Textures, Scope = _textures,
            Skin = Skin, CommandPresentation = CommandPresentation, ContentRoot = ContentRoot,
            OverlayHost = OverlayHost, Music = Music, SceneMedia = SceneMedia, ReadSettings = ReadSettings,
            FocusIncomingPlayer = FocusIncomingPlayer,
            IntroduceNewGame = IntroduceNewGame, StartNewGame = StartNewGame, LoadGame = () => SystemMenu.Open(Persistence.SaveLoadMode.Load),
            PresentMapRecord = PresentMapRecord, PresentMapSpeech = PresentMapSpeech,
            PresentExecutionMessages = _feedback.PresentExecutionMessages,
            FocusRecord = _feedback.FocusRecord,
            IsSpeechComplete = _feedback.IsComplete, DismissSpeech = _feedback.Dismiss
        };
        AddChild(_presentation);
    }
}
