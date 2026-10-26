using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Owns military command intentions, their windows, and the map target input lease.</summary>
public partial class MilitaryCommandFlow : Node
{
    public MapWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public event Action? Changed;
    public event Action<string>? Notice;
    public event Action<StrategySnapshot, GameCommand, IReadOnlyList<DomainEvent>>? Submitted;
    public MilitaryTargets? Targets { get; private set; }
    public MapObjectTarget? Source { get; private set; }
    public bool IsActive => Source is not null;
    public MilitaryCommandStage Stage => Source is null ? MilitaryCommandStage.None :
        Targets is null ? MilitaryCommandStage.Preparation :
        _policy is not null || _catalog is not null ? MilitaryCommandStage.TargetModal : MilitaryCommandStage.Targets;
    private MapTargetInputLease? _input;
    private DeploymentView? _preparation;
    private SpecialDeploymentView? _specialPreparation;
    private MilitaryPolicyView? _policy;
    private CatalogView? _catalog;

    public override void _Process(double delta)
    {
        var view = Targets?.View ?? _preparation?.Draft.View ?? _specialPreparation?.Draft.View;
        if (view is not null && view.World.Revision != Session.Snapshot.Revision) CloseCommand();
    }
    public void OpenSpecial(int settlementId, SpecialDeploymentKind kind)
    {
        if (IsActive) return;
        var view = Session.View;
        if (SpecialDeploymentDraft.UnavailableReasonKey(view,settlementId,kind) is string reason)
        { Notice?.Invoke(Texts.Resolve(reason)); return; }
        Textures.RequestPrefixes("deployment-","detail-","catalog-","information-","roster-","message-","narration-","dialog-");
        Source = new(settlementId,false);
        _specialPreparation = new() { Draft = new(view,settlementId,kind), Texts=Texts, Textures=Textures, Service=Service,
            Closed=CloseCommand, Proceed=() =>
            {
                if (_specialPreparation is not { Draft.CanProceed:true } preparation) return;
                Targets = new SpecialDeploymentTargets(preparation.Draft); preparation.Hide(); BindTargets();
            } };
        AddChild(_specialPreparation); Changed?.Invoke();
    }

    public void Open(int settlementId)
    {
        if (IsActive) return;
        var view = Session.View;
        if (DeploymentDraft.UnavailableReasonKey(view, settlementId) is string reason)
        { Notice?.Invoke(Texts.Resolve(reason)); return; }
        Textures.RequestPrefixes("deployment-", "formation-", "detail-", "catalog-", "information-", "roster-", "message-", "narration-", "dialog-");
        Source = new(settlementId, false);
        _preparation = new DeploymentView
        {
            Draft = new(view, settlementId), Session = Session, Texts = Texts,
            Textures = Textures, Service = Service, Proceed = BeginTargetSelection, Closed = CloseCommand
        };
        AddChild(_preparation); Changed?.Invoke();
    }
    private void BeginTargetSelection()
    {
        if (_preparation is not { Draft.CanProceed: true } preparation) return;
        Targets = new DeploymentTargets(preparation.Draft);
        preparation.Hide();
        BindTargets();
    }

    public void OpenUnit(int unitId, UnitCommandKind kind)
    {
        if (IsActive) return;
        var view = Session.View;
        string command = UnitCommandTargets.CommandId(kind);
        if (!MapCommandMenu.Unit(view, unitId).Any(e => e.Id == command && e.Enabled)) return;
        Textures.RequestPrefixes("deployment-", "catalog-", "information-", "roster-", "message-", "narration-", "dialog-");
        Source = new(unitId, true);
        Targets = new UnitCommandTargets(view, unitId, kind);
        BindTargets();
    }

    private void BindTargets()
    {
        World.Display!.Targets = Targets;
        _input?.Dispose();
        _input = new(World, PickTarget, cell => Targets?.SetWaypoint(cell),
            Targets is SpecialDeploymentTargets { IsConstruction:true } construction ? cell =>
        {
            if (construction.At(cell) is { } target) PickMilitaryTarget(target);
            else construction.SetWaypoint(cell);
        } : null);
        Changed?.Invoke();
    }

    private void PickTarget(MapObjectTarget? target)
    {
        if (target is null) { EndTargetSelection(); return; }
        var selected = new MilitaryTarget(target.Value.Id, target.Value.IsUnit);
        PickMilitaryTarget(selected);
    }
    private void PickMilitaryTarget(MilitaryTarget selected)
    {
        if (Targets is not { } targets) return;
        if (targets.Selected == selected)
        {
            if (targets.BeginPolicies()) ShowPolicies();
        }
        else targets.Select(selected);
        if (targets.Error is { } error) Notice?.Invoke(error);
    }

    private void ShowPolicies()
    {
        if (Targets is not { } targets || _policy is not null) return;
        CloseTargetList();
        _policy = new() { Selection = targets, Texts = Texts, Textures = Textures, Closed = ClosePolicies, Submit = SubmitCommand };
        AddChild(_policy);
    }

    private CommandRejection SubmitCommand(bool confirmAttack)
    {
        if (Targets is not { } targets || targets.View.World.Revision != Session.Snapshot.Revision)
            return CommandRejection.NotStable;
        var before = targets.View;
        var command = targets.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence, confirmAttack);
        var result = Session.Execute(new("player"), command);
        if (result.Submission.Accepted)
        {
            CloseCommand();
            Submitted?.Invoke(before, command, result.Events);
        }
        return result.Submission.Rejection;
    }

    private void ClosePolicies()
    {
        if (_policy is not { } policy) return;
        _policy = null; policy.GetParent().RemoveChild(policy); policy.QueueFree();
        Targets?.ReturnToTargets();
    }

    public void EndTargetSelection()
    {
        ClosePolicies();
        CloseTargetList();
        ReleaseTargets();
        if (_preparation is null && _specialPreparation is null) Source = null;
        _preparation?.Show(); _specialPreparation?.Show(); Changed?.Invoke();
    }

    private void CloseCommand()
    {
        if (Targets is not null) EndTargetSelection();
        if (_preparation is { } preparation)
        { _preparation = null; RemoveChild(preparation); preparation.QueueFree(); }
        if (_specialPreparation is { } special)
        { _specialPreparation = null; RemoveChild(special); special.QueueFree(); }
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
        var catalog = new MilitaryTargetCatalog(targets, Texts);
        _catalog = new CatalogView
        {
            Title = targets is SpecialDeploymentTargets { IsConstruction:true } ? "选择地域" : "选择对象",
            HintText = targets is SpecialDeploymentTargets { IsConstruction:true }
                ? Texts.ResolveRich(San9.Content.Interface.SpecialDeploymentText.Key("site")) : "请选择目标。", CloseTitle = "中止",
            Textures = Textures, Service = Service,
            Pages = catalog.DisplayPages,
            ReadRows = _ => catalog.Rows,
            Activated = index =>
            {
                var target = catalog.Target(index); CloseTargetList(); PickMilitaryTarget(target);
                if (target.Position is { } position) World.FocusCell(San9.Core.Domain.MapCoordinates.Cell(position));
                else if (target.IsUnit) World.FocusUnit(target.Id); else World.FocusBuilding(target.Id);
            },
            ShowMap = CloseTargetList,
            Closed = EndTargetSelection
        };
        AddChild(_catalog);
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (Targets is not null && _catalog is null && _policy is null &&
            input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); EndTargetSelection(); }
    }
    private void ReleaseTargets()
    {
        if (World.Display is { } display && ReferenceEquals(display.Targets, Targets)) display.Targets = null;
        Targets = null;
        _input?.Dispose(); _input = null;
    }
    public override void _ExitTree()
    {
        Changed = null; Notice = null; Submitted = null;
        ReleaseTargets();
        Source = null;
        _preparation = null; _specialPreparation = null; _policy = null; _catalog = null;
    }
}
