using Godot;

namespace San9.Client.Godot;

/// <summary>Owns overlay windows and their input restrictions across either tree-exit order.</summary>
internal sealed class WorkflowWindows(StrategyOverlayHost host) : IDisposable
{
    private readonly Dictionary<Control, Action> _windows = [];
    private bool _disposed;
    public bool IsEmpty => _windows.Count == 0;
    public void Show(Control view, Action? released = null, InteractionBlock blocked = InteractionBlock.Orders)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        void Release()
        {
            view.TreeExiting -= Release;
            _windows.Remove(view);
            released?.Invoke();
        }
        _windows.Add(view, Release);
        view.TreeExiting += Release;
        try { host.AddWorkflow(view, blocked); }
        catch { Close(view); throw; }
    }
    public void Close(Control view)
    {
        if (_windows.TryGetValue(view, out var release)) release();
        if (view.GetParent() is { } parent) parent.RemoveChild(view);
        view.QueueFree();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var view in _windows.Keys.ToArray()) Close(view);
    }
}
