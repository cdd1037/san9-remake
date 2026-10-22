using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class PatrolCommandFlow : CommandWorkflow
{
    private PatrolView? _patrol;
    public MapObjectTarget? Source => _patrol is { } view ? new(view.Draft.Home.Id,false) : null;
    public override void _Process(double delta)
    {
        if (_patrol is { } view && view.Draft.View.World.Revision != Session.Snapshot.Revision) Close();
    }
    public void Open(int id)
    {
        try { OpenCore(id); }
        catch { Close(); throw; }
    }
    private void OpenCore(int id)
    {
        if (HasWindow) return;
        if (PatrolDraft.UnavailableReasonKey(Session.View,id) is string reason) { Notice(Texts.Resolve(reason)); return; }
        if (CommandPresentation.Patrol is null) { Notice(CommandText.Resolve(Texts, "content.patrol")); return; }
        Scope.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _patrol = new() { Draft = new(Session.View,id), Texts = Texts, Service = Textures, Textures = Scope,
            Closed = Close, Submit = SubmitPatrol };
        ShowWindow(_patrol, () => { _patrol = null; });
    }
    public override void Close()
    {
        if (_patrol is not { } view) return;
        _patrol = null; CloseWindow(view);
    }
    private void SubmitPatrol()
    {
        try { SubmitPatrolCore(); }
        catch { Close(); throw; }
    }
    private void SubmitPatrolCore()
    {
        if (_patrol is not { Draft.CanExecute: true } view) return;
        if (view.Draft.View.World.Revision != Session.Snapshot.Revision) { Close(); return; }
        var before = view.Draft.View;
        var messages = Session.Messages(Texts);
        var result = Session.Execute(new("player"),view.Draft.CreateCommand(Guid.NewGuid(),Session.Snapshot.Simulation.NextSequence));
        if (!result.Submission.Accepted)
        { Close(); Notice(CommandText.Resolve(Texts, "failure.patrol")); return; }
        var completed = result.Events.OfType<DevelopmentCompletedEvent>().Single();
        var sequence = PatrolPresentation.Create(before,messages,CommandPresentation.Patrol!,completed,Random.Shared.Next);
        Close();
        OpenNarration(sequence, before);
    }
}
