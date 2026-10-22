using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private MilitaryTargets? _targets => _flow?.Targets;
    private bool SelectingTargets => _targets is not null || _personnelFlow?.IsTargeting == true || _commandTargets is not null;
    private MilitaryCommandFlow _flow = null!;
    private Label _targetTitle = null!;
    private readonly List<StrategyButton> _targetButtons = [];

    private void BuildTargetControls()
    {
        _flow = new() { World = World, Session = Session, Texts = Texts, Service = Textures, Textures = _textures };
        _flow.Changed += RefreshMilitaryCommand;
        _flow.Notice += message => { _notice.SetNotice(message); _notice.Show(); };
        _flow.Submitted += PresentMilitaryCommand;
        OverlayHost.AddChild(_flow);
        _personnelFlow = new() { World = World, Session = Session, Texts = Texts, Service = Textures,
            Textures = _textures, OverlayHost = OverlayHost, Definition = CommandPresentation.Personnel };
        _personnelFlow.Changed += RefreshMilitaryCommand;
        _personnelFlow.Notice += message => { _notice.SetNotice(message); _notice.Show(); };
        _personnelFlow.Narrate += NarratePersonnel;
        AddChild(_personnelFlow);
        _targetTitle = LabelAt(CommandText.Resolve(Texts, "select.object"), new(61, 12), new(104, 32), 24, HorizontalAlignment.Left);
        _text.Apply(_targetTitle, new(24, new Color("f8f8b0"), GameTextEffect.Outline, new Color("780000")));
        _targetTitle.Hide();
        foreach (var (role, title, x, action) in new (string, string, int, Action)[]
            { ("list", CommandText.Resolve(Texts, "action.list"), 834, () => { if (_commandTargets is not null) ShowCommandTargetsList(); else if (_personnelFlow?.IsTargeting == true) _personnelFlow.ShowTargetList(); else _flow.ShowTargetList(); }),
              ("cancel", CommandText.Resolve(Texts, "action.cancel"), 921, () => { if (_commandTargets is not null) EndCommandTargets(); else if (_personnelFlow?.IsTargeting == true) _personnelFlow.EndTargets(); else _flow.EndTargetSelection(); }) })
        {
            var button = new StrategyButton { Title = title, Position = new(x, 680), Size = new(88, 44), Visible = false,
                Frames = Enumerable.Range(0, 4).Select(f => (Texture2D)_textures.Get($"target-{role}-{f}", this)).ToArray(),
                TextRole = role == "list" ? GameButtonTextRole.ConfirmYes : GameButtonTextRole.ConfirmNo };
            button.Pressed += action;
            _bottomRight.AddChild(button); _targetButtons.Add(button); _hudButtons.Add(button);
        }
    }

    private void PresentMilitaryCommand(StrategySnapshot before, GameCommand command, IReadOnlyList<DomainEvent> events)
    {
        var after = Session.View;
        var sequence = MilitaryCommandPresentation.Create(before, after, Session.Messages(Texts),
            CommandPresentation, command, events, Random.Shared.Next);
        OpenNarration(sequence, after);
    }

    private IEnumerable<(string Asset, Vector2 Position)> VisibleFrames()
    {
        foreach (var frame in _frames)
        {
            if (frame.Asset == "sidebar-frame")
            {
                if (SidebarVisible) yield return (frame.Asset, frame.Position + GroupPosition(_sidebarMotion));
                continue;
            }
            if (SelectingTargets && frame.Asset != "radar-frame") continue;
            if (frame.Asset == "ruler-frame" && !Turn.IsStrategy) continue;
            yield return (frame.Asset, frame.Position + GroupPosition(frame.Asset == "radar-frame" ? _topRight : _topLeft));
        }
        if (!SelectingTargets) yield break;
        yield return ("target-title-left", Vector2.Zero);
        for (int x = 64; x < 160; x += 24) yield return ("target-title-middle", new(x, 0));
        yield return ("target-title-right", new(160, 0));
        yield return ("target-footer", GroupPosition(_bottomRight) + new Vector2(808, 657));
    }

    private void RefreshMilitaryCommand()
    {
        bool targeting = SelectingTargets;
        _targetTitle.Text = _commandTargets?.Title ?? (_personnelFlow?.IsTargeting == true && _personnelFlow.IsRegionSelection ? CommandText.Resolve(Texts, "select.region") :
            _flow.Targets is SpecialDeploymentTargets { IsConstruction:true } ? CommandText.Resolve(Texts, "select.build-site") : CommandText.Resolve(Texts, "select.object"));
        _targetTitle.Visible = targeting;
        foreach (var button in _targetButtons) button.Visible = targeting;
        ClearHover(); UpdateTurnControls(); QueueRedraw();
    }
}
