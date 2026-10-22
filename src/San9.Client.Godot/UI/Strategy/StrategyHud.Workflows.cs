using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public CommandPresentation CommandPresentation { get; init; } = CommandPresentation.Empty;
    public string ContentRoot { get; init; } = "";
    private CommandTargetFlow _commandTargets = null!;
    private PatrolCommandFlow _patrolFlow = null!;
    private AppointmentCommandFlow _appointmentFlow = null!;
    private FacilityCommandFlow _facilityFlow = null!;
    private DemolitionCommandFlow _demolitionFlow = null!;
    private MissionCommandFlow _missionFlow = null!;
    private CorpsCommandFlow _corpsFlow = null!;
    private void BuildCommandFlows()
    {
        _commandTargets = new() { World = World, Session = Session };
        _commandTargets.Changed += RefreshMilitaryCommand;
        AddChild(_commandTargets);
        var services = new CommandWorkflowServices(Session, World, Texts, Textures, _textures, Skin,
            CommandPresentation, OverlayHost, _commandTargets,
            message => { _notice.SetNotice(message); _notice.Show(); },
            (messages, view, completed) => _presentation.OpenNarration(messages, view, completed),
            PresentMapRecord, ClearCommandSelection);
        _patrolFlow = new() { Services = services }; AddChild(_patrolFlow);
        _appointmentFlow = new() { Services = services }; AddChild(_appointmentFlow);
        _facilityFlow = new() { Services = services }; AddChild(_facilityFlow);
        _demolitionFlow = new() { Services = services }; AddChild(_demolitionFlow);
        _missionFlow = new() { Services = services }; AddChild(_missionFlow);
        _corpsFlow = new() { Services = services }; AddChild(_corpsFlow);
    }
    private void ClearCommandSelection()
    {
        _contextTarget = null; _hoverTarget = null;
        _mapContext.Close(); _commandMenu.Close();
        Selection.Clear(); World.HoverObject(null); _hoverCard.ShowInfo(null, Vector2.Zero);
    }
}
