using San9.Content.Text;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class MissionCommandFlow : CommandWorkflow
{
    private MissionCommandView? _mission;
    private (int? Actor, int? Target, int? Secondary, San9.Core.Simulation.GiftOffer? Gift)? _missionAdviceSelection;
    public void Open(int id, string kind)
    {
        try { OpenCore(id, kind); }
        catch { Close(); throw; }
    }
    private void OpenCore(int id, string kind)
    {
        if (HasWindow) return;
        if (CommandPresentation.Missions.Count == 0)
        { Notice(CommandText.Resolve(Texts, "content.missions")); return; }
        Scope.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "narration-", "dialog-");
        _missionAdviceSelection = null;
        _mission = new() { Draft = new(Session,id,kind), Session = Session, Texts = Texts, Service = Textures, Textures = Scope, Skin = Skin,
            World = World, Closed = Close, Submit = SubmitMission, SelectionChanged = ShowMissionAdvice, ChooseMap = Targets.Begin };
        ShowWindow(_mission, () => { _mission = null; });
    }
    public override void Close()
    {
        if (_mission is not {} view) return;
        _mission = null; CloseWindow(view);
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
        try { SubmitMissionCore(); }
        catch { Close(); throw; }
    }
    private void SubmitMissionCore()
    {
        if (_mission is not {} view) return;
        if (!view.Draft.CanExecute) { view.ShowFailure(CommandFailureText.Describe(Texts, view.Draft.Rejection)); return; }
        var before = view.Draft.View;
        var result = Session.Execute(new("player"),view.Draft.CreateCommand());
        if (!result.Submission.Accepted) { view.ShowFailure(CommandFailureText.Describe(Texts, result.Submission.Rejection)); return; }
        var messages = MissionPresentation.Departure(view.Draft,CommandPresentation.Missions,Session.Messages(Texts,before.World),Texts);
        Close(); if (messages.Count > 0) OpenNarration(messages,before);
    }
}
