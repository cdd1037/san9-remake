using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private PersonnelCommandFlow _personnelFlow = null!;
    private void NarratePersonnel(IReadOnlyList<CommandMessage> sequence, StrategySnapshot before, string? initialSound = null)
    {
        if (sequence.Count == 0) return;
        _textures.RequestPrefixes("narration-","dialog-","information-");
        _narration = new() { Messages = sequence, View = before, Texts = Texts, Service = Textures, Textures = _textures,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, ReadMessageSpeed = () => ReadSettings().MessageSpeed, InitialSoundRole = initialSound,
            Closed = () => CloseNarration() };
        OverlayHost.AddWorkflow(_narration, InteractionBlock.Orders | InteractionBlock.Continuation | InteractionBlock.Playback);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_commandTargets is { Owner.Visible: false } && input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
        { GetViewport().SetInputAsHandled(); EndCommandTargets(); return; }
        if (_demolition is not null && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); CloseDemolition(); return; }
        if (_personnelFlow?.CanCancelTargets == true && _narration is null &&
            input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
        { GetViewport().SetInputAsHandled(); _personnelFlow.EndTargets(); }
    }
}
