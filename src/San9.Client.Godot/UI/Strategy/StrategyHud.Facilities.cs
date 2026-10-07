using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private FacilityCommandView? _facilityCommand;
    private MapObjectTarget? FacilitySource => _facilityCommand is {} view ? new(view.Draft.Home.Id,false) : null;
    private void OpenFacilityCommand(int id,string kind)
    {
        if (FacilityCommandDraft.UnavailableReasonKey(Session.View,id,kind) is string reason)
        { _notice.SetNotice(Texts.Resolve(reason)); _notice.Show(); return; }
        if (!CommandPresentation.Facilities.ContainsKey(kind))
        { _notice.SetNotice("内容包缺少设施指令表现，请重新转换内容包。"); _notice.Show(); return; }
        _textures.RequestPrefixes("deployment-","detail-","catalog-","information-","narration-","dialog-");
        _facilityCommand = new() { Draft = new(Session,id,kind), Texts = Texts, Service = Textures, Textures = _textures,
            Closed = CloseFacilityCommand, Submit = SubmitFacilityCommand };
        OverlayHost.AddChild(_facilityCommand);
    }
    private void CloseFacilityCommand()
    {
        if (_facilityCommand is not {} view) return;
        _facilityCommand = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void SubmitFacilityCommand()
    {
        if (_facilityCommand is not { Draft.CanExecute: true } view) return;
        var before = view.Draft.View;
        var messages = Session.Messages(Texts);
        var result = Session.Execute(new("player"),view.Draft.CreateCommand(Guid.NewGuid(),Session.Snapshot.Simulation.NextSequence));
        if (!result.Submission.Accepted)
        { CloseFacilityCommand(); _notice.SetNotice("指令未能执行，请重新选择设施和武将。"); _notice.Show(); return; }
        var completed = result.Events.Single(e => e is DevelopmentCompletedEvent or FoodTradeCompletedEvent);
        var sequence = FacilityCommandPresentation.Create(before,messages,CommandPresentation,view.Draft.Kind,completed,Random.Shared.Next);
        CloseFacilityCommand();
        _narration = new() { Messages = sequence, View = before, Texts = Texts, Service = Textures, Textures = _textures,
            ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, Closed = CloseNarration };
        OverlayHost.AddChild(_narration);
    }
}
