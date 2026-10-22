using San9.Content.Text;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class FacilityCommandFlow : CommandWorkflow
{
    private FacilityCommandView? _facilityCommand;
    public MapObjectTarget? Source => _facilityCommand is {} view ? new(view.Draft.Home.Id,false) : null;
    public override void _Process(double delta)
    {
        if (_facilityCommand is { } view && !view.Draft.IsCurrent) Close();
    }
    public void Open(int id,string kind)
    {
        try { OpenCore(id, kind); }
        catch { Close(); throw; }
    }
    private void OpenCore(int id,string kind)
    {
        if (HasWindow) return;
        if (FacilityCommandDraft.UnavailableReasonKey(Session.View,id,kind) is string reason)
        { Notice(Texts.Resolve(reason)); return; }
        if (!CommandPresentation.Facilities.ContainsKey(kind))
        { Notice(CommandText.Resolve(Texts, "content.facilities")); return; }
        Scope.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _facilityCommand = new() { Draft = new(Session,id,kind), Texts = Texts, Service = Textures, Textures = Scope,
            Closed = Close, Submit = SubmitFacilityCommand };
        ShowWindow(_facilityCommand, () => { _facilityCommand = null; });
    }
    public override void Close()
    {
        if (_facilityCommand is not {} view) return;
        _facilityCommand = null; CloseWindow(view);
    }
    private void SubmitFacilityCommand()
    {
        try { SubmitFacilityCommandCore(); }
        catch { Close(); throw; }
    }
    private void SubmitFacilityCommandCore()
    {
        if (_facilityCommand is not { Draft.CanExecute: true } view) return;
        var before = view.Draft.View;
        var messages = Session.Messages(Texts);
        var result = Session.Execute(new("player"),view.Draft.CreateCommand(Guid.NewGuid(),Session.Snapshot.Simulation.NextSequence));
        if (!result.Submission.Accepted)
        { Close(); Notice(CommandText.Resolve(Texts, "failure.command")); return; }
        var completed = result.Events.Single(e => e is DevelopmentCompletedEvent or FoodTradeCompletedEvent);
        var sequence = FacilityCommandPresentation.Create(before,messages,CommandPresentation,view.Draft.Kind,completed,Random.Shared.Next);
        Close();
        OpenNarration(sequence, before);
    }
}
