using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public CommandPresentation CommandPresentation { get; init; } = CommandPresentation.Empty;
    public string ContentRoot { get; init; } = "";
    private PatrolView? _patrol;
    private MapObjectTarget? PatrolSource => _patrol is { } view ? new(view.Draft.Home.Id,false) : null;
    private void OpenPatrol(int id)
    {
        if (PatrolDraft.UnavailableReasonKey(Session.View,id) is string reason) { _notice.SetNotice(Texts.Resolve(reason)); _notice.Show(); return; }
        if (CommandPresentation.Patrol is null) { _notice.SetNotice(CommandText.Resolve(Texts, "content.patrol")); _notice.Show(); return; }
        _textures.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _patrol = new() { Draft = new(Session.View,id), Session = Session, Texts = Texts, Service = Textures, Textures = _textures,
            Closed = ClosePatrol, Submit = SubmitPatrol };
        OverlayHost.AddChild(_patrol);
    }
    private void ClosePatrol()
    {
        if (_patrol is not { } view) return;
        _patrol = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void SubmitPatrol()
    {
        if (_patrol is not { Draft.CanExecute: true } view) return;
        if (view.Draft.View.World.Revision != Session.Snapshot.Revision) { ClosePatrol(); return; }
        var before = view.Draft.View;
        var messages = Session.Messages(Texts);
        var result = Session.Execute(new("player"),view.Draft.CreateCommand(Guid.NewGuid(),Session.Snapshot.Simulation.NextSequence));
        if (!result.Submission.Accepted)
        { ClosePatrol(); _notice.SetNotice(CommandText.Resolve(Texts, "failure.patrol")); _notice.Show(); return; }
        var completed = result.Events.OfType<DevelopmentCompletedEvent>().Single();
        var sequence = PatrolPresentation.Create(before,messages,CommandPresentation.Patrol!,completed,Random.Shared.Next);
        ClosePatrol();
        OpenNarration(sequence, before);
    }
}
