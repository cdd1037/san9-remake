using San9.Content.Text;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private AppointmentView? _appointment;
    private MapObjectTarget? AppointmentSource => _appointment is { } view ? new(view.Draft.SourceId, false) : null;
    private void OpenAppointment(int sourceId, string kind)
    {
        if (AppointmentDraft.UnavailableReasonKey(Session.View, sourceId, kind) is string reason)
        { _notice.SetNotice(Texts.Resolve(reason)); _notice.Show(); return; }
        if (kind == "strategist" && CommandPresentation.Strategist is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.strategist")); _notice.Show(); return; }
        if (kind == "reward" && CommandPresentation.Reward is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.reward")); _notice.Show(); return; }
        if (kind == "exile" && CommandPresentation.Exile is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.exile")); _notice.Show(); return; }
        if (kind is "bestow" or "confiscate" && CommandPresentation.TreasureTransfer is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.treasures")); _notice.Show(); return; }
        if (kind == "execute" && CommandPresentation.Execution is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.execution")); _notice.Show(); return; }
        if (kind == "rank" && CommandPresentation.Offices is null)
        { _notice.SetNotice(CommandText.Resolve(Texts, "content.rank")); _notice.Show(); return; }
        _textures.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "dialog-", "narration-");
        if (kind == "rank") _textures.RequestPrefixes("text-icon-arrow-");
        _appointment = new() { Draft = new(Session, sourceId, kind), Texts = Texts, Service = Textures, Textures = _textures,
            Closed = CloseAppointment, Submit = SubmitAppointment };
        OverlayHost.AddChild(_appointment);
    }
    private void CloseAppointment()
    {
        if (_appointment is not { } view) return;
        _appointment = null; OverlayHost.RemoveChild(view); view.QueueFree();
    }
    private void SubmitAppointment()
    {
        if (_appointment is not { Draft.CanExecute: true } view) return;
        var draft = view.Draft;
        var beforeMessages = Session.Messages(Texts);
        var result = draft.Submit();
        CloseAppointment();
        if (draft.Kind == "strategist" && result.Rejection == CommandRejection.None)
        {
            var sequence = StrategistPresentation.Create(draft.View, Session.Messages(Texts), CommandPresentation.Strategist!,
                result.Events.OfType<AdviserAppointedEvent>().Single(), Random.Shared.Next);
            OpenNarration(sequence, Session.View);
            return;
        }
        var execution = result.Events.OfType<PrisonerExecutedEvent>().SingleOrDefault();
        if (result.CompletedCommands > 0 && (draft.Kind is "rank" or "reward" or "exile" or "bestow" or "confiscate" || execution is not null))
        {
            var publication=ExecutionPublication.Read(draft.View,Session.View,beforeMessages,Texts,CommandPresentation,result.Events);
            foreach(var record in publication.Records) PresentMapRecord(record);
            var sequence = execution is not null ? publication.Messages
                : draft.Kind == "rank" ? OfficePresentation.Create(draft.View,beforeMessages,CommandPresentation.Offices!,result.Events.OfType<OfficeAssignedEvent>(),Random.Shared.Next)
                : draft.Kind == "reward" ? RewardPresentation.Create(draft.View, beforeMessages, CommandPresentation.Reward!,
                result.Events.OfType<OfficerRewardedEvent>().ToArray(), Random.Shared.Next)
                : draft.Kind == "exile" ? ExilePresentation.Create(draft.View, beforeMessages, CommandPresentation.Exile!, result.Events, Random.Shared.Next)
                : TreasureTransferPresentation.Create(draft.View,beforeMessages,CommandPresentation.TreasureTransfer!,
                    result.Events.OfType<TreasureTransferredEvent>().Single(),result.Events.OfType<OfficerLoyaltyChangedEvent>(),Random.Shared.Next);
            OpenNarration(execution is not null ? sequence : sequence.Concat(publication.Messages).ToArray(),draft.View,
                () => {
                    if (result.Rejection != CommandRejection.None)
                    { _notice.SetNotice(AppointmentPresentation.Result(draft, Texts, result)); _notice.Show(); }
                });
            return;
        }
        _notice.SetNotice(AppointmentPresentation.Result(draft, Texts, result)); _notice.Show();
    }
}
