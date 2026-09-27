using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Owns the deployment draft, its windows, and the map target input lease.</summary>
public partial class DeploymentFlow : Node
{
    public MapWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public event Action? Changed;
    public event Action<string>? Notice;
    public DeploymentTargets? Targets { get; private set; }
    public MapObjectTarget? Source { get; private set; }
    public bool IsActive => _preparation is not null;
    public DeploymentStage Stage => _preparation is null ? DeploymentStage.None :
        Targets is null ? DeploymentStage.Preparation :
        _policy is not null || _catalog is not null ? DeploymentStage.TargetModal : DeploymentStage.Targets;
    private DeploymentView? _preparation;
    private DeploymentPolicyView? _policy;
    private CatalogView? _catalog;

    public void Open(int settlementId)
    {
        if (IsActive) return;
        var view = Session.View;
        if (DeploymentDraft.UnavailableReason(view, settlementId) is string reason)
        { Notice?.Invoke(reason); return; }
        Textures.RequestPrefixes("deployment-", "formation-", "detail-", "catalog-", "information-", "roster-", "message-", "dialog-");
        Source = new(settlementId, false);
        _preparation = new DeploymentView
        {
            Draft = new(view, settlementId), Session = Session, Texts = Texts,
            Textures = Textures, Service = Service, Proceed = BeginTargetSelection, Closed = CloseDeployment
        };
        AddChild(_preparation); Changed?.Invoke();
    }
    private void BeginTargetSelection()
    {
        if (_preparation is not { Draft.CanProceed: true } preparation) return;
        Targets = new(preparation.Draft);
        preparation.Hide();
        World.Display!.Targets = Targets;
        World.TargetPicked = PickTarget;
        World.WaypointPicked = cell => Targets?.SetWaypoint(cell);
        Changed?.Invoke();
    }

    private void PickTarget(MapObjectTarget? target)
    {
        if (target is null) { EndTargetSelection(); return; }
        if (Targets is not { } targets) return;
        var selected = new DeploymentTarget(target.Value.Id, target.Value.IsUnit);
        if (targets.Selected == selected)
        {
            if (targets.BeginPolicies()) ShowDeploymentPolicies();
        }
        else targets.Select(selected);
        if (targets.Error is { } error) Notice?.Invoke(error);
    }

    private void ShowDeploymentPolicies()
    {
        if (Targets is not { } targets || _policy is not null) return;
        CloseTargetList();
        _policy = new() { Selection = targets, Texts = Texts, Textures = Textures, Closed = CloseDeploymentPolicies, Submit = SubmitDeployment };
        AddChild(_policy);
    }

    private CommandRejection SubmitDeployment(bool confirmAttack)
    {
        if (Targets is not { } targets || targets.Draft.View.World.Revision != Session.Snapshot.Revision)
            return CommandRejection.NotStable;
        var result = Session.Execute(new("player"), targets.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence, confirmAttack));
        if (result.Submission.Accepted) CloseDeployment();
        return result.Submission.Rejection;
    }

    private void CloseDeploymentPolicies()
    {
        if (_policy is not { } policy) return;
        _policy = null; policy.GetParent().RemoveChild(policy); policy.QueueFree();
        Targets?.ReturnToTargets();
    }

    public void EndTargetSelection()
    {
        CloseDeploymentPolicies();
        CloseTargetList();
        Targets = null;
        if (World.Display is { } display) display.Targets = null;
        World.TargetPicked = null;
        World.WaypointPicked = null;
        _preparation?.Show(); Changed?.Invoke();
    }

    private void CloseDeployment()
    {
        if (Targets is not null) EndTargetSelection();
        if (_preparation is not { } preparation) return;
        _preparation = null; RemoveChild(preparation); preparation.QueueFree();
        Source = null; Changed?.Invoke();
    }

    private void CloseTargetList()
    {
        if (_catalog is not { } catalog) return;
        _catalog = null; catalog.GetParent().RemoveChild(catalog); catalog.QueueFree();
    }

    public void ShowTargetList()
    {
        if (Targets is not { } targets || _catalog is not null) return;
        var entries = targets.Candidates.Keys.OrderBy(t => t.IsUnit).ThenBy(t => t.Id).ToArray();
        var rows = entries.Select((t, index) =>
        {
            var info = t.IsUnit ? UnitMapInteraction.Hover(World.View, Texts, t.Id) : MapHoverInfo.Resolve(World.View, Texts, t.Id);
            return new CatalogRow(index, [info?.Name ?? "", t.IsUnit ? "部队" : "设施", info?.Force is { } force ? Texts.Resolve(force.NameKey) : "----"]);
        }).ToArray();
        _catalog = new CatalogView
        {
            Title = "选择对象", HintText = "双击目标定位。", Textures = Textures, Service = Service, ZIndex = 30,
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
        AddChild(_catalog);
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (Targets is not null && _catalog is null && _policy is null &&
            input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); EndTargetSelection(); }
    }
    public override void _ExitTree()
    {
        if (World.Display is { } display) display.Targets = null;
        World.TargetPicked = null; World.WaypointPicked = null;
    }
}
