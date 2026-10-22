using San9.Content.Text;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class AppointmentCommandFlow : CommandWorkflow
{
    private AppointmentView? _appointment;
    public MapObjectTarget? Source => _appointment is { } view ? new(view.Draft.SourceId, false) : null;
    public override void _Process(double delta)
    {
        if (_appointment is { } view && !view.Draft.IsCurrent) Close();
    }
    public void Open(int sourceId, string kind)
    {
        try { OpenCore(sourceId, kind); }
        catch { Close(); throw; }
    }
    private void OpenCore(int sourceId, string kind)
    {
        if (HasWindow) return;
        if (AppointmentDraft.UnavailableReasonKey(Session.View, sourceId, kind) is string reason)
        { Notice(Texts.Resolve(reason)); return; }
        if (kind == "strategist" && CommandPresentation.Strategist is null)
        { Notice(CommandText.Resolve(Texts, "content.strategist")); return; }
        if (kind == "reward" && CommandPresentation.Reward is null)
        { Notice(CommandText.Resolve(Texts, "content.reward")); return; }
        if (kind == "exile" && CommandPresentation.Exile is null)
        { Notice(CommandText.Resolve(Texts, "content.exile")); return; }
        if (kind is "bestow" or "confiscate" && CommandPresentation.TreasureTransfer is null)
        { Notice(CommandText.Resolve(Texts, "content.treasures")); return; }
        if (kind == "execute" && CommandPresentation.Execution is null)
        { Notice(CommandText.Resolve(Texts, "content.execution")); return; }
        if (kind == "rank" && CommandPresentation.Offices is null)
        { Notice(CommandText.Resolve(Texts, "content.rank")); return; }
        Scope.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "dialog-", "narration-");
        if (kind == "rank") Scope.RequestPrefixes("text-icon-arrow-");
        _appointment = new() { Draft = new(Session, sourceId, kind), Texts = Texts, Service = Textures, Textures = Scope,
            Closed = Close, Submit = SubmitAppointment };
        ShowWindow(_appointment, () => { _appointment = null; });
    }
    public override void Close()
    {
        if (_appointment is not { } view) return;
        _appointment = null; CloseWindow(view);
    }
    private void SubmitAppointment()
    {
        try { SubmitAppointmentCore(); }
        catch { Close(); throw; }
    }
    private void SubmitAppointmentCore()
    {
        if (_appointment is not { Draft.CanExecute: true } view) return;
        var draft = view.Draft;
        var beforeMessages = Session.Messages(Texts);
        var result = draft.Submit();
        Close();
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
            var publication=ExecutionPublication.Read(draft.View,Session.View,beforeMessages,Texts,CommandPresentation,result.Events,executionMap:false);
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
                    { Notice(AppointmentPresentation.Result(draft, Texts, result)); }
                });
            return;
        }
        Notice(AppointmentPresentation.Result(draft, Texts, result));
    }
}
