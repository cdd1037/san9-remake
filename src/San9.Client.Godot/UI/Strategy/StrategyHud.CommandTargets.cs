using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public sealed record CommandTargetRequest(Control Owner, CommandTargetSelection Selection, string Title,
    Action<Action<int>,Action> ShowList, Action<int> Selected, Action Cancelled);

public partial class StrategyHud
{
    private CommandTargetRequest? _commandTargets;
    private MilitaryCommandStage CommandTargetStage => _commandTargets is not {} request ? MilitaryCommandStage.None :
        request.Owner.Visible ? MilitaryCommandStage.TargetModal : MilitaryCommandStage.Targets;

    private void BeginCommandTargets(CommandTargetRequest request)
    {
        _commandTargets = request; request.Owner.Hide();
        World.Display!.CommandTargets = request.Selection;
        World.TargetPicked = target =>
        {
            if (target is null) EndCommandTargets();
            else if (request.Selection.Resolve(new CommandMapObject(target.Value.Id,target.Value.IsUnit)) is int id) PickCommandTarget(id);
        };
        World.WaypointPicked = null;
        World.SitePicked = null;
        RefreshMilitaryCommand();
    }
    private void ShowCommandTargetsList()
    {
        if (_commandTargets is not {} request) return;
        request.ShowList(PickCommandTarget, () => { request.Owner.Hide(); RefreshMilitaryCommand(); });
        RefreshMilitaryCommand();
    }
    private void PickCommandTarget(int id)
    {
        if (_commandTargets is not {} request || !request.Selection.Candidates.Contains(id)) return;
        bool current = request.Selection.View.World.Revision == Session.Snapshot.Revision;
        EndCommandTargets(cancelled: !current);
        if (current) request.Selected(id);
    }
    private void EndCommandTargets(bool cancelled = true)
    {
        if (_commandTargets is not {} request) return;
        _commandTargets = null;
        if (World.Display is {} display) display.CommandTargets = null;
        World.TargetPicked = null; World.WaypointPicked = null; World.SitePicked = null;
        request.Owner.Show();
        if (cancelled) request.Cancelled();
        RefreshMilitaryCommand();
    }
}
