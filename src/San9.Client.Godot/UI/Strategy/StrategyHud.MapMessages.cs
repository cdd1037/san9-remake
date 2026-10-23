using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private MapFeedbackFlow _feedback = null!;
    private void BuildMapMessages()
    {
        _feedback = new() { World = World, Textures = Textures, Skin = Skin, ContentRoot = ContentRoot,
            CommandPresentation = CommandPresentation, Panels = _executionPanels, Minimap = _minimap!,
            SceneSounds = _sceneSounds, SoundEnabled = () => SoundEnabled, Obstacles = MapObstacles,
            InputBlocked = IsBlockedPoint, ModalOpen = () => OverlayHost.HasModal || _notice.Visible,
            CanRevealRecords = () => Turn.Phase == San9.Core.Simulation.ResolutionPhase.Daily || Turn.IsStrategy,
            MouseFilter = MouseFilterEnum.Ignore, Size = Size };
        AddChild(_feedback);
    }
    public bool PresentMapRecord(MapMessageRecord entry) => _feedback.PresentMapRecord(entry);
    public bool PresentMapSpeech(MapSpeechMessage message) => _feedback.PresentMapSpeech(message);
    public bool IsSpeechPoint(Vector2 point) => _feedback?.IsSpeechPoint(point) == true;
}
