using Godot;
using San9.Application;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class Main : Node
{
    private GameSession _session = null!;
    private Label _clockLabel = null!;
    private Label _hashLabel = null!;
    private long _commandSequence;

    public override void _Ready()
    {
        WorldState initialState = WorldState.Create(
            new GameClock(190, 1, XunIndex.Early),
            seed: 190UL);
        _session = new GameSession(initialState);
        _clockLabel = GetNode<Label>("UiLayer/UiRoot/Panel/Clock");
        _hashLabel = GetNode<Label>("UiLayer/UiRoot/Panel/Hash");
        Render(_session.Snapshot);
    }

    public void OnAdvancePressed()
    {
        WaitCommand command = new(
            Guid.NewGuid(),
            new ForceId(1),
            _commandSequence++);
        CommandSubmissionResult submission = _session.Submit(command);
        if (!submission.Accepted)
        {
            _hashLabel.Text = $"Command rejected: {submission.Rejection}";
            return;
        }

        AdvanceXunResult result = _session.AdvanceXun();
        Render(result.Snapshot);
    }

    private void Render(WorldSnapshot snapshot)
    {
        _clockLabel.Text = $"{snapshot.Clock.Year} 年 {snapshot.Clock.Month} 月 · 第 {(int)snapshot.Clock.Xun} 旬";
        _hashLabel.Text = $"状态哈希：{snapshot.StateHash[..16]}…";
    }
}
