using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private MilitaryTargets? _targets => _flow?.Targets;
    private bool SelectingTargets => _targets is not null || _personnelTargeting;
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
        _targetTitle = LabelAt("选择对象", new(61, 12), new(104, 32), 24, HorizontalAlignment.Left);
        _text.Apply(_targetTitle, new(24, new Color("f8f8b0"), GameTextEffect.Outline, new Color("780000")));
        _targetTitle.Hide();
        foreach (var (role, title, x, action) in new (string, string, int, Action)[]
            { ("list", "一览", 834, () => { if (_personnelTargeting) ShowPersonnelTargetsList(); else _flow.ShowTargetList(); }),
              ("cancel", "中止", 921, () => { if (_personnelTargeting) EndPersonnelTargets(); else _flow.EndTargetSelection(); }) })
        {
            var button = new StrategyButton { Title = title, Position = new(x, 680), Size = new(88, 44), Visible = false,
                Frames = Enumerable.Range(0, 4).Select(f => (Texture2D)_textures[$"target-{role}-{f}"]).ToArray(),
                TextRole = role == "list" ? GameButtonTextRole.ConfirmYes : GameButtonTextRole.ConfirmNo };
            button.Pressed += action;
            AddChild(button); _targetButtons.Add(button);
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
                if (SidebarVisible) yield return (frame.Asset, frame.Position + new Vector2(_sidebarOffset, 0));
                continue;
            }
            if (SelectingTargets && frame.Asset != "radar-frame") continue;
            if (frame.Asset == "ruler-frame" && !Turn.IsStrategy) continue;
            yield return frame;
        }
        if (!SelectingTargets) yield break;
        yield return ("target-title-left", Vector2.Zero);
        for (int x = 64; x < 160; x += 24) yield return ("target-title-middle", new(x, 0));
        yield return ("target-title-right", new(160, 0));
        yield return ("target-footer", new(808, 657));
    }

    private void RefreshMilitaryCommand()
    {
        bool targeting = SelectingTargets;
        _targetTitle.Text = _personnelTargeting && _personnel?.Draft.Kind == "search" ? "选择地域" :
            _flow.Targets is SpecialDeploymentTargets { IsConstruction:true } ? "建设地点" : "选择对象";
        _targetTitle.Visible = targeting;
        foreach (var button in _targetButtons) button.Visible = targeting;
        ClearHover(); UpdateTurnControls(); QueueRedraw();
    }
}
