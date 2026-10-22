using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private PersonnelCommandFlow _personnelFlow = null!;
    public override void _UnhandledInput(InputEvent input)
    {
        if (_commandTargets?.CanCancel == true && input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
        { GetViewport().SetInputAsHandled(); _commandTargets.End(); return; }
        if (_demolitionFlow?.IsActive == true && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); _demolitionFlow.Close(); return; }
        if (_personnelFlow?.CanCancelTargets == true && _presentation?.HasNarration != true &&
            input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
        { GetViewport().SetInputAsHandled(); _personnelFlow.EndTargets(); }
    }
}
