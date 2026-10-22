using San9.Content.Text;
using Godot;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class DemolitionCommandFlow : CommandWorkflow
{
    public bool IsActive => _demolition is not null;
    private GameDialog? _demolition;
    private FacilityDemolitionDraft? _demolitionDraft;
    public MapObjectTarget? Source => _demolitionDraft is { } draft ? new(draft.SettlementId, false) : null;

    public void Open(int id)
    {
        try { OpenCore(id); }
        catch { Close(); throw; }
    }
    private void OpenCore(int id)
    {
        if (HasWindow) return;
        if (FacilityDemolitionDraft.UnavailableReasonKey(Session.View, id) is string reason)
        { Notice(Texts.Resolve(reason)); return; }
        if (CommandPresentation.Demolition is not { } definition)
        { Notice(CommandText.Resolve(Texts, "content.demolition")); return; }
        Scope.RequestPrefixes("dialog-", "narration-");
        _demolitionDraft = new(Session, id);
        _demolition = new() { Textures = Scope };
        _demolition.SetRichMessage(_demolitionDraft.Confirmation(Session.Messages(Texts), definition).BbCode);
        _demolition.Confirmed += SubmitDemolition;
        _demolition.Dismissed += Close;
        ShowWindow(_demolition, () => { _demolition = null; _demolitionDraft = null; });
        _demolition.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _demolition.FocusDefaultButton();
    }

    public override void Close()
    {
        _demolitionDraft = null;
        if (_demolition is not { } dialog) return;
        _demolition = null;
        CloseWindow(dialog);
    }

    private void SubmitDemolition()
    {
        try { SubmitDemolitionCore(); }
        catch { Close(); throw; }
    }
    private void SubmitDemolitionCore()
    {
        if (_demolitionDraft is not { } draft) return;
        if (!draft.CanExecute) { Close(); return; }
        var before = draft.View;
        var context = Session.Messages(Texts);
        var result = Session.Execute(new("player"), draft.CreateCommand(Guid.NewGuid(), Session.Snapshot.Simulation.NextSequence));
        Close();
        if (!result.Submission.Accepted)
        { Notice(CommandText.Resolve(Texts, "failure.demolition")); return; }
        var sequence = FacilityDemolitionPresentation.Create(before, context, CommandPresentation.Demolition!,
            result.Events.OfType<FacilityDemolishedEvent>().Single(), Random.Shared.Next);
        OpenNarration(sequence, before);
    }
}
