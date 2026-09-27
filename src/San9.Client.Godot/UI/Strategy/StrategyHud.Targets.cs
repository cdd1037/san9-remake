using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private DeploymentTargets? _targets;
    private Label _targetTitle = null!;
    private readonly List<StrategyButton> _targetButtons = [];
    private CatalogView? _targetCatalog;
    private DeploymentPolicyView? _deploymentPolicy;

    private void BuildTargetControls()
    {
        _targetTitle = LabelAt("选择对象", new(61, 12), new(104, 32), 24, HorizontalAlignment.Left);
        _text.Apply(_targetTitle, new(24, new Color("f8f8b0"), GameTextEffect.Outline, new Color("780000")));
        _targetTitle.Hide();
        foreach (var (role, title, x, action) in new (string, string, int, Action)[]
            { ("list", "一览", 834, ShowTargetList), ("cancel", "中止", 921, EndTargetSelection) })
        {
            var button = new StrategyButton { Title = title, Position = new(x, 680), Size = new(88, 44), Visible = false,
                Frames = Enumerable.Range(0, 4).Select(f => (Texture2D)_textures[$"target-{role}-{f}"]).ToArray(),
                TextRole = role == "list" ? GameButtonTextRole.ConfirmYes : GameButtonTextRole.ConfirmNo };
            button.Pressed += action;
            AddChild(button); _targetButtons.Add(button);
        }
    }

    private IEnumerable<(string Asset, Vector2 Position)> VisibleFrames()
    {
        foreach (var frame in _frames)
        {
            if (_targets is not null && frame.Asset != "radar-frame") continue;
            if (frame.Asset == "sidebar-frame" && (_commandMenu?.Visible == true || !Turn.IsStrategy)) continue;
            if (frame.Asset == "ruler-frame" && !Turn.IsStrategy) continue;
            yield return frame;
        }
        if (_targets is null) yield break;
        yield return ("target-title-left", Vector2.Zero);
        for (int x = 64; x < 160; x += 24) yield return ("target-title-middle", new(x, 0));
        yield return ("target-title-right", new(160, 0));
        yield return ("target-footer", new(808, 657));
    }

    private void BeginTargetSelection()
    {
        if (_deployment is not { Draft.CanProceed: true } preparation) return;
        _targets = new(preparation.Draft);
        preparation.Hide(); Overview.Close();
        World.Display!.Targets = _targets;
        World.TargetPicked = PickTarget;
        World.WaypointPicked = cell => _targets?.SetWaypoint(cell);
        _targetTitle.Show(); foreach (var button in _targetButtons) button.Show();
        ClearHover(); UpdateTurnControls(); QueueRedraw();
    }

    private void PickTarget(MapObjectTarget? target)
    {
        if (target is null) { EndTargetSelection(); return; }
        if (_targets is not { } targets) return;
        var selected = new DeploymentTarget(target.Value.Id, target.Value.IsUnit);
        if (targets.Selected == selected)
        {
            if (targets.BeginPolicies()) ShowDeploymentPolicies();
        }
        else targets.Select(selected);
        if (targets.Error is { } error) { _notice.SetNotice(error); _notice.Show(); }
    }

    private void ShowDeploymentPolicies()
    {
        if (_targets is not { } targets || _deploymentPolicy is not null) return;
        CloseTargetList(); ClearHover();
        _deploymentPolicy = new() { Selection = targets, Texts = Texts, Textures = _textures, Closed = CloseDeploymentPolicies, Submit = SubmitDeployment };
        GetParent().AddChild(_deploymentPolicy);
    }

    private CommandRejection SubmitDeployment(bool confirmAttack)
    {
        if (_targets is not { } targets || targets.Draft.View.World.Revision != Session.Snapshot.Revision)
            return CommandRejection.NotStable;
        var result = Session.Execute(new("player"), targets.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence, confirmAttack));
        if (result.Submission.Accepted) CloseDeployment();
        return result.Submission.Rejection;
    }

    private void CloseDeploymentPolicies()
    {
        if (_deploymentPolicy is not { } policy) return;
        _deploymentPolicy = null; policy.GetParent().RemoveChild(policy); policy.QueueFree();
        _targets?.ReturnToTargets();
    }

    private void EndTargetSelection()
    {
        CloseDeploymentPolicies();
        CloseTargetList();
        _targets = null;
        if (World.Display is { } display) display.Targets = null;
        World.TargetPicked = null;
        World.WaypointPicked = null;
        _targetTitle.Hide(); foreach (var button in _targetButtons) button.Hide();
        _deployment?.Show(); ClearHover(); UpdateTurnControls(); QueueRedraw();
    }

    private void CloseDeployment()
    {
        if (_targets is not null) EndTargetSelection();
        if (_deployment is not { } preparation) return;
        _deployment = null; preparation.GetParent().RemoveChild(preparation); preparation.QueueFree();
    }

    private void CloseTargetList()
    {
        if (_targetCatalog is not { } catalog) return;
        _targetCatalog = null; catalog.GetParent().RemoveChild(catalog); catalog.QueueFree();
    }

    private void ShowTargetList()
    {
        if (_targets is not { } targets || _targetCatalog is not null) return;
        var entries = targets.Candidates.Keys.OrderBy(t => t.IsUnit).ThenBy(t => t.Id).ToArray();
        var rows = entries.Select((t, index) =>
        {
            var info = t.IsUnit ? UnitMapInteraction.Hover(Scenario, Texts, t.Id) : MapHoverInfo.Resolve(Scenario, Texts, t.Id);
            return new CatalogRow(index, [info?.Name ?? "", t.IsUnit ? "部队" : "设施", info?.Force is { } force ? Texts.Resolve(force.NameKey) : "----"]);
        }).ToArray();
        _targetCatalog = new CatalogView
        {
            Title = "选择对象", HintText = "双击目标定位。", Textures = _textures, Service = Textures, ZIndex = 30,
            Pages = [new("目标", [new("name", "名称", 18, CatalogAlignment.Center),
                new("kind", "类别", 8, CatalogAlignment.Center), new("force", "势力", 14, CatalogAlignment.Center)])],
            ReadRows = _ => rows,
            Activated = index =>
            {
                var target = entries[index]; CloseTargetList(); PickTarget(new(target.Id, target.IsUnit));
                if (target.IsUnit) World.FocusUnit(target.Id); else World.FocusBuilding(target.Id);
            },
            Closed = CloseTargetList
        };
        GetParent().AddChild(_targetCatalog);
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_targets is not null && _targetCatalog is null && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { if (_deploymentPolicy is not null) CloseDeploymentPolicies(); else EndTargetSelection(); GetViewport().SetInputAsHandled(); }
    }
}
