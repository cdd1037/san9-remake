using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private GameDialog? _demolition;
    private FacilityDemolitionDraft? _demolitionDraft;
    private MapObjectTarget? DemolitionSource => _demolitionDraft is { } draft ? new(draft.SettlementId, false) : null;

    private void OpenDemolition(int id)
    {
        if (FacilityDemolitionDraft.UnavailableReasonKey(Session.View, id) is string reason)
        { _notice.SetNotice(Texts.Resolve(reason)); _notice.Show(); return; }
        if (CommandPresentation.Demolition is not { } definition)
        { _notice.SetNotice("内容包缺少撤除表现，请重新转换内容包。"); _notice.Show(); return; }
        _textures.RequestPrefixes("dialog-", "narration-");
        _demolitionDraft = new(Session, id);
        _demolition = new() { Textures = _textures };
        _demolition.SetRichMessage(_demolitionDraft.Confirmation(Session.Messages(Texts), definition).BbCode);
        _demolition.Confirmed += SubmitDemolition;
        _demolition.Dismissed += CloseDemolition;
        OverlayHost.AddChild(_demolition);
        _demolition.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _demolition.FocusDefaultButton();
    }

    private void CloseDemolition()
    {
        _demolitionDraft = null;
        if (_demolition is not { } dialog) return;
        _demolition = null;
        OverlayHost.RemoveChild(dialog);
        dialog.QueueFree();
    }

    private void SubmitDemolition()
    {
        if (_demolitionDraft is not { } draft) return;
        if (!draft.CanExecute) { CloseDemolition(); return; }
        var before = draft.View;
        var context = Session.Messages(Texts);
        var result = Session.Execute(new("player"), draft.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence));
        CloseDemolition();
        if (!result.Submission.Accepted)
        { _notice.SetNotice("撤除未能执行，请重新选择设施。"); _notice.Show(); return; }
        var sequence = FacilityDemolitionPresentation.Create(before, context, CommandPresentation.Demolition!,
            result.Events.OfType<FacilityDemolishedEvent>().Single(), Random.Shared.Next);
        OpenNarration(sequence, before);
    }
}
