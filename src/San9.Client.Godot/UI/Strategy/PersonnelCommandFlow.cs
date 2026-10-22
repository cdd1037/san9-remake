using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Owns the personnel draft, its window and its map targeting lifecycle.</summary>
public partial class PersonnelCommandFlow : Node
{
    public MapWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public StrategyOverlayHost OverlayHost { get; init; } = null!;
    public PersonnelPresentationDefinition? Definition { get; init; }
    public event Action? Changed;
    public event Action<string>? Notice;
    public event Action<IReadOnlyList<CommandMessage>, StrategySnapshot, string?>? Narrate;
    private PersonnelCommandView? _view;
    private MapTargetInputLease? _input;
    public bool IsTargeting { get; private set; }
    public bool IsActive => _view is not null;
    public bool IsRegionSelection => _view?.Draft.Kind == "search";
    public bool CanCancelTargets => IsTargeting && _view?.HasChild == false;
    private (int? Actor, int? Target)? _adviceSelection;
    public MapObjectTarget? Source => _view is {} view ? new(view.Draft.Home.Id,false) : null;
    public MilitaryCommandStage Stage => _view is null ? MilitaryCommandStage.None :
        !IsTargeting ? MilitaryCommandStage.Preparation : _view.HasChild ? MilitaryCommandStage.TargetModal : MilitaryCommandStage.Targets;
    public void Open(int id, string kind)
    {
        if (IsActive) return;
        if (PersonnelDraft.UnavailableReasonKey(Session.View,id,kind) is string reason)
        { Notice?.Invoke(Texts.Resolve(reason)); return; }
        if (Definition is null)
        { Notice?.Invoke(CommandText.Resolve(Texts, "content.personnel")); return; }
        Textures.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _adviceSelection = null;
        _view = new() { Draft = new(Session,id,kind), Texts = Texts, Service = Service, Textures = Textures,
            Closed = Close, Submit = Submit, ChooseMap = BeginTargets, SelectionChanged = ShowAdvice };
        _view.TreeExiting += Release;
        try { OverlayHost.AddWorkflow(_view); ShowAdvice(); }
        catch { Close(); throw; }
    }
    public void Close()
    {
        if (_view is not {} view) return;
        view.TreeExiting -= Release;
        Release();
        if (view.GetParent() is { } parent) parent.RemoveChild(view);
        view.QueueFree();
        Changed?.Invoke();
    }
    private void BeginTargets()
    {
        if (_view is not {} view) return;
        IsTargeting = true; view.Hide();
        World.Display!.PersonnelTargets = view.Draft;
        _input = new(World, target =>
        {
            if (target is null) EndTargets();
            else if (!target.Value.IsUnit) PickTarget(target.Value.Id);
        }, cell => { if (view.Draft.PlaceAt(cell) is int place) PickTarget(place); });
        Changed?.Invoke();
    }
    private void PickTarget(int id)
    {
        if (_view?.Draft.SetTarget(id) != true) return;
        EndTargets(); _view.Rebuild(); ShowAdvice();
    }
    public void EndTargets()
    {
        if (!IsTargeting) return;
        ReleaseTargets();
        _view?.Show(); Changed?.Invoke();
    }
    private void ReleaseTargets()
    {
        if (!IsTargeting) return;
        IsTargeting = false;
        if (World.Display is { } display && ReferenceEquals(display.PersonnelTargets, _view?.Draft))
            display.PersonnelTargets = null;
        _input?.Dispose(); _input = null;
    }

    // Also used when the overlay host leaves the tree before this controller.
    private void Release()
    {
        ReleaseTargets();
        _view = null;
        _adviceSelection = null;
    }

    public void ShowTargetList() => _view?.ShowTargetList(() => _view?.Hide(), PickTarget);
    private void ShowAdvice()
    {
        if (_view is not {} view || Definition is null) return;
        var selection = (view.Draft.RepresentativeId, view.Draft.TargetId);
        if (_adviceSelection == selection) return;
        _adviceSelection = selection;
        var sequence = PersonnelPresentation.Advice(view.Draft,Definition,Session.Messages(Texts),Texts,Random.Shared.Next);
        Narrate?.Invoke(sequence, view.Draft.View, null);
    }
    private void Submit()
    {
        if (_view is not { Draft.CanExecute:true } view || Definition is null) return;
        var before = view.Draft.View;
        IReadOnlyList<CommandMessage> sequence;
        try
        {
            var context = Session.Messages(Texts);
            var result = Session.Execute(new("player"), view.Draft.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence));
            if (!result.Submission.Accepted)
            { Notice?.Invoke(CommandText.Resolve(Texts, "failure.command")); return; }
            sequence = PersonnelPresentation.Departure(view.Draft, Definition, context, Texts, Random.Shared.Next);
        }
        finally { Close(); }
        Narrate?.Invoke(sequence, before, Definition.DepartureSound);
    }
    public override void _Process(double delta)
    {
        if (_view is { } view && !view.Draft.IsCurrent) Close();
    }

    public override void _ExitTree()
    {
        Changed = null;
        Notice = null;
        Narrate = null;
        Close();
    }
}
