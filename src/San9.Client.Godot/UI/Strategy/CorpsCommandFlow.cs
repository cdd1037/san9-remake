using San9.Content.Text;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class CorpsCommandFlow : CommandWorkflow
{
    private CorpsCommandView? _corpsCommand;
    public void Open()
    {
        try { OpenCore(); }
        catch { Close(); throw; }
    }
    private void OpenCore()
    {
        if (HasWindow) return;
        Scope.RequestPrefixes("deployment-", "detail-", "catalog-", "information-", "dialog-", "decision-");
        _corpsCommand = new() { Draft = new(Session), Preferences = Services.Preferences, Session = Session, Texts = Texts, Service = Textures, Textures = Scope, Skin = Skin,
            Closed = Close, Submit = SubmitCorpsManagement, ChooseMap = Targets.Begin };
        ShowWindow(_corpsCommand, () => { _corpsCommand = null; });
    }
    public override void Close()
    {
        if (_corpsCommand is not {} view) return;
        _corpsCommand = null; CloseWindow(view);
    }
    private void SubmitCorpsManagement()
    {
        try { SubmitCorpsManagementCore(); }
        catch { Close(); throw; }
    }
    private void SubmitCorpsManagementCore()
    {
        if (_corpsCommand is not {} view) return;
        if (!view.Draft.CanExecute) { view.ShowFailure(CommandFailureText.Describe(Texts, view.Draft.Rejection)); return; }
        var result = Session.Execute(new("player"),view.Draft.CreateCommand());
        if (!result.Submission.Accepted) { view.ShowFailure(CommandFailureText.Describe(Texts, result.Submission.Rejection)); return; }
        var messages = CorpsPresentation.Results(view.Draft.View, result.Events, Texts, CommandPresentation.Missions,
            Session.Messages(Texts,view.Draft.View.World),Session.Messages(Texts));
        var publication = ExecutionPublication.Read(view.Draft.View, Session.View, Session.Messages(Texts, view.Draft.View.World),
            Texts, CommandPresentation, result.Events, executionMap: false);
        Close();
        Services.ClearMapSelection();
        OpenNarration(messages, view.Draft.View, () => PresentExecution(publication, view.Draft.View));
    }
}
