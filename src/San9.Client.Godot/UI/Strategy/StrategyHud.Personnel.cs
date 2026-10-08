using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private PersonnelCommandView? _personnel;
    private bool _personnelTargeting;
    private (int? Actor, int? Target)? _personnelAdviceSelection;
    private MapObjectTarget? PersonnelSource => _personnel is {} view ? new(view.Draft.Home.Id,false) : null;
    private MilitaryCommandStage PersonnelStage => _personnel is null ? MilitaryCommandStage.None :
        !_personnelTargeting ? MilitaryCommandStage.Preparation : _personnel.HasChild ? MilitaryCommandStage.TargetModal : MilitaryCommandStage.Targets;
    private void OpenPersonnel(int id, string kind)
    {
        if (PersonnelDraft.UnavailableReasonKey(Session.View,id,kind) is string reason)
        { _notice.SetNotice(Texts.Resolve(reason)); _notice.Show(); return; }
        if (CommandPresentation.Personnel is null)
        { _notice.SetNotice("内容包缺少人才命令表现，请重新转换内容包。"); _notice.Show(); return; }
        _textures.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _personnelAdviceSelection = null;
        _personnel = new() { Draft = new(Session,id,kind), Texts = Texts, Service = Textures, Textures = _textures,
            Closed = ClosePersonnel, Submit = SubmitPersonnel, ChooseMap = BeginPersonnelTargets, SelectionChanged = ShowPersonnelAdvice };
        OverlayHost.AddChild(_personnel);
        ShowPersonnelAdvice();
    }
    private void ClosePersonnel()
    {
        if (_personnel is not {} view) return;
        EndPersonnelTargets(); _personnel = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void BeginPersonnelTargets()
    {
        if (_personnel is not {} view) return;
        _personnelTargeting = true; view.Hide();
        World.Display!.PersonnelTargets = view.Draft;
        World.TargetPicked = target =>
        {
            if (target is null) EndPersonnelTargets();
            else if (!target.Value.IsUnit) PickPersonnelTarget(target.Value.Id);
        };
        World.WaypointPicked = cell => { if (view.Draft.PlaceAt(cell) is int place) PickPersonnelTarget(place); };
        World.SitePicked = null;
        RefreshMilitaryCommand();
    }
    private void PickPersonnelTarget(int id)
    {
        if (_personnel?.Draft.SetTarget(id) != true) return;
        EndPersonnelTargets(); _personnel.Rebuild(); ShowPersonnelAdvice();
    }
    private void EndPersonnelTargets()
    {
        if (!_personnelTargeting) return;
        _personnelTargeting = false;
        if (World.Display is {} display) display.PersonnelTargets = null;
        World.TargetPicked = null; World.WaypointPicked = null; World.SitePicked = null;
        _personnel?.Show(); RefreshMilitaryCommand();
    }
    private void ShowPersonnelTargetsList() => _personnel?.ShowTargetList(() => _personnel?.Hide(), PickPersonnelTarget);
    private void ShowPersonnelAdvice()
    {
        if (_personnel is not {} view || CommandPresentation.Personnel is null) return;
        var selection = (view.Draft.Members.Length == 1 ? (int?)view.Draft.Members[0] : null, view.Draft.TargetId);
        if (_personnelAdviceSelection == selection) return;
        _personnelAdviceSelection = selection;
        var sequence = PersonnelPresentation.Advice(view.Draft,CommandPresentation.Personnel,Session.Messages(Texts),Texts,Random.Shared.Next);
        NarratePersonnel(sequence,view.Draft.View);
    }
    private void NarratePersonnel(IReadOnlyList<CommandMessage> sequence, StrategySnapshot before, string? initialSound = null)
    {
        if (sequence.Count == 0) return;
        _textures.RequestPrefixes("narration-","dialog-","information-");
        _narration = new() { Messages = sequence, View = before, Texts = Texts, Service = Textures, Textures = _textures,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, InitialSoundRole = initialSound,
            MessagePresented = m => _executionPanels.Append(m.Message), Closed = CloseNarration };
        OverlayHost.AddChild(_narration);
    }
    private void SubmitPersonnel()
    {
        if (_personnel is not { Draft.CanExecute:true } view || CommandPresentation.Personnel is null) return;
        var before = view.Draft.View;
        var context = Session.Messages(Texts);
        var result = Session.Execute(new("player"),view.Draft.CreateCommand(Guid.NewGuid(),Session.Snapshot.Simulation.NextSequence));
        if (!result.Submission.Accepted)
        { ClosePersonnel(); _notice.SetNotice("指令未能执行，请重新选择设施和武将。"); _notice.Show(); return; }
        var sequence = PersonnelPresentation.Departure(view.Draft,CommandPresentation.Personnel,context,Texts,Random.Shared.Next);
        ClosePersonnel(); NarratePersonnel(sequence,before,CommandPresentation.Personnel.DepartureSound);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_personnelTargeting && _personnel?.HasChild == false && _narration is null &&
            input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape })
        { GetViewport().SetInputAsHandled(); EndPersonnelTargets(); }
    }
}
