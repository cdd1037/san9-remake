using San9.Content.Text;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private MissionCommandView? _mission;
    private CorpsCommandView? _corpsCommand;
    private (int? Actor, int? Target, int? Secondary, San9.Core.Simulation.GiftOffer Gift)? _missionAdviceSelection;
    private void OpenMission(int id, string kind)
    {
        if (CommandPresentation.Missions.Count == 0)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.missions")); _notice.Show(); return; }
        _textures.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "narration-", "dialog-");
        _missionAdviceSelection = null;
        _mission = new() { Draft = new(Session,id,kind), Session = Session, Texts = Texts, Service = Textures, Textures = _textures, Skin = Skin,
            World = World, Closed = CloseMission, Submit = SubmitMission, SelectionChanged = ShowMissionAdvice, ChooseMap = BeginCommandTargets };
        OverlayHost.AddChild(_mission);
    }
    private void CloseMission()
    {
        if (_mission is not {} view) return;
        if (_commandTargets?.Owner == view) EndCommandTargets();
        _mission = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void ShowMissionAdvice()
    {
        if (_mission is not {} view) return;
        var selection = (view.Draft.ActorId,view.Draft.TargetId,view.Draft.SecondaryId,view.Draft.Gift);
        if (_missionAdviceSelection == selection) return;
        _missionAdviceSelection = selection;
        var messages = MissionPresentation.Advice(view.Draft,CommandPresentation.Missions,Session.Messages(Texts),Texts);
        if (messages.Count > 0) OpenNarration(messages,view.Draft.View);
    }
    private void SubmitMission()
    {
        if (_mission is not {} view) return;
        if (!view.Draft.CanExecute) { view.ShowFailure(CommandFailureText.Describe(Texts, view.Draft.Rejection)); return; }
        var before = view.Draft.View;
        var result = Session.Execute(new("player"),view.Draft.CreateCommand());
        if (!result.Submission.Accepted) { view.ShowFailure(CommandFailureText.Describe(Texts, result.Submission.Rejection)); return; }
        var messages = MissionPresentation.Departure(view.Draft,CommandPresentation.Missions,Session.Messages(Texts,before.World),Texts);
        CloseMission(); if (messages.Count > 0) OpenNarration(messages,before);
    }
    private void OpenCorpsManagement()
    {
        _textures.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "dialog-", "decision-");
        _corpsCommand = new() { Draft = new(Session), Session = Session, Texts = Texts, Service = Textures, Textures = _textures, Skin = Skin,
            Closed = CloseCorpsManagement, Submit = SubmitCorpsManagement, ChooseMap = BeginCommandTargets };
        OverlayHost.AddChild(_corpsCommand);
    }
    private void CloseCorpsManagement()
    {
        if (_corpsCommand is not {} view) return;
        if (_commandTargets?.Owner == view) EndCommandTargets();
        _corpsCommand = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void SubmitCorpsManagement()
    {
        if (_corpsCommand is not {} view) return;
        if (!view.Draft.CanExecute) { view.ShowFailure(CommandFailureText.Describe(Texts, view.Draft.Rejection)); return; }
        var result = Session.Execute(new("player"),view.Draft.CreateCommand());
        if (!result.Submission.Accepted) { view.ShowFailure(CommandFailureText.Describe(Texts, result.Submission.Rejection)); return; }
        var messages = CorpsPresentation.Results(view.Draft.View, result.Events, Texts, CommandPresentation.Missions,
            Session.Messages(Texts,view.Draft.View.World),Session.Messages(Texts));
        CloseCorpsManagement();
        _contextTarget = null; _hoverTarget = null;
        _mapContext.Close(); _commandMenu.Close();
        Selection.Clear(); World.HoverObject(null); _hoverCard.ShowInfo(null, global::Godot.Vector2.Zero);
        OpenNarration(messages, view.Draft.View);
    }
}
