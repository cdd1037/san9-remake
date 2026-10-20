using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class SystemMenuPanel
{
    private void OpenExit()
    {
        if (_busy) return;
        _textures!.RequestPrefixes("dialog-", "decision-choice-");
        List<(string Text, Action Choose)> options = [(ExitMessage("exit"), ConfirmExit)];
        var view = Session.View;
        if (Session.CanChangeForceControl && view.PlayerForceId is int forceId)
        {
            options.Add((ExitMessage("delegate-current", forceId), () => ConfirmHandOver("confirm-current",
                new ForceControlOperation(forceId, ControllerKind.Automatic), forceId)));
        }
        if (Session.CanChangeForceControl && view.World.Simulation.Controls.Any(c =>
            c.Kind == ControllerKind.Player && c.ForceId != view.PlayerForceId))
            options.Add((ExitMessage("delegate-all"), () => ConfirmHandOver("confirm-all", new HandOverAllForcesOperation())));
        options.Add(("中止", Close));
        ReplacePage(new VerticalChoiceDialog { Textures = _textures, Options = options, RichOptions = true });
    }

    private void ConfirmExit()
    {
        var dialog = MapDialog();
        dialog.SetRichMessage(ExitMessage(Session.HasUnsavedChanges ? "confirm-exit-unsaved" : "confirm-exit"));
        dialog.Confirmed += () => GetTree().Quit();
        dialog.Dismissed += Close;
        dialog.FocusDefaultButton();
    }

    private void ConfirmHandOver(string role, SessionOperation operation, int? forceId = null)
    {
        var dialog = MapDialog();
        dialog.SetRichMessage(ExitMessage(Session.HasUnsavedChanges ? role + "-unsaved" : role, forceId));
        dialog.Dismissed += Close;
        dialog.Confirmed += () => HandOver(operation);
        dialog.FocusDefaultButton();
    }

    private string ExitMessage(string role, int? forceId = null)
    {
        Dictionary<string, MessageBinding> bindings = [];
        if (forceId is int force)
        {
            int ruler = Session.View.Forces.Single(f => f.Id == force).LeaderId
                ?? throw new InvalidOperationException("Control delegation requires a ruler.");
            bindings["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, ruler);
        }
        return Session.Messages(Texts).Produce(ExitMessages[role], bindings,
            _ => throw new InvalidDataException("Exit menu messages must not require randomness.")).BbCode;
    }

    private void HandOver(SessionOperation operation)
    {
        if (_busy) return;
        var result = Session.Apply(Session.Request(operation));
        if (result.Disposition != SessionDisposition.Accepted)
        {
            var notice = MapDialog();
            notice.SetNotice($"交接失败：{result.Rejection}");
            notice.FocusDefaultButton();
            return;
        }
        // Rebuild selection and HUD only after the input callback releases the current page.
        _busy = true;
        var token = _lifetime.Token;
        Callable.From(() => { if (!token.IsCancellationRequested) ControlDelegated(); }).CallDeferred();
    }
}
