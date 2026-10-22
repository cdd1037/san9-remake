using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public sealed record CommandTargetRequest(Control Owner, CommandTargetSelection Selection, string Title,
    Action<Action<int>,Action> ShowList, Action<int> Selected, Action Cancelled);

public partial class CommandTargetFlow : Node
{
    public MapWorld World { get; init; } = null!;
    public San9.Application.GameSession Session { get; init; } = null!;
    public event Action? Changed;
    private MapTargetInputLease? _input;
    private CommandTargetRequest? _commandTargets;
    public bool IsActive => _commandTargets is not null;
    public string? Title => _commandTargets?.Title;
    public bool CanCancel => _commandTargets is { Owner.Visible: false };
    public MilitaryCommandStage Stage => _commandTargets is not {} request ? MilitaryCommandStage.None :
        request.Owner.Visible ? MilitaryCommandStage.TargetModal : MilitaryCommandStage.Targets;

    public void Begin(CommandTargetRequest request)
    {
        End();
        _commandTargets = request; request.Owner.TreeExiting += OwnerExiting; request.Owner.Hide();
        World.Display!.CommandTargets = request.Selection;
        _input = new(World, target =>
        {
            if (target is null) End();
            else if (request.Selection.Resolve(new CommandMapObject(target.Value.Id,target.Value.IsUnit)) is int id) PickCommandTarget(id);
        });
        Changed?.Invoke();
    }
    public void ShowList()
    {
        if (_commandTargets is not {} request) return;
        request.ShowList(PickCommandTarget, () => { request.Owner.Hide(); Changed?.Invoke(); });
        Changed?.Invoke();
    }
    public void ReleaseOwner(Control owner)
    {
        if (_commandTargets?.Owner == owner) { Release(); Changed?.Invoke(); }
    }
    private void OwnerExiting() => Release();
    private void Release()
    {
        if (_commandTargets is not { } request) return;
        request.Owner.TreeExiting -= OwnerExiting;
        _commandTargets = null;
        if (World.Display is { } display && ReferenceEquals(display.CommandTargets, request.Selection)) display.CommandTargets = null;
        _input?.Dispose(); _input = null;
    }
    public override void _Process(double delta)
    {
        if (_commandTargets is { } request && request.Selection.View.World.Revision != Session.Snapshot.Revision) End();
    }
    public override void _ExitTree() { Changed = null; Release(); }

    private void PickCommandTarget(int id)
    {
        if (_commandTargets is not {} request || !request.Selection.Candidates.Contains(id)) return;
        bool current = request.Selection.View.World.Revision == Session.Snapshot.Revision;
        End(cancelled: !current);
        if (current) request.Selected(id);
    }
    public void End(bool cancelled = true)
    {
        if (_commandTargets is not {} request) return;
        Release();
        request.Owner.Show();
        if (cancelled) request.Cancelled();
        Changed?.Invoke();
    }
}
