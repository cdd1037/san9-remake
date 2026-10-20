namespace San9.Client.Godot;

[Flags]
public enum InteractionBlock { None = 0, Orders = 1, Continuation = 2, Playback = 4 }

/// <summary>Each active workflow owns its restrictions until its lifetime ends, even while hidden.</summary>
public sealed class InteractionLocks
{
    private readonly Dictionary<object, InteractionBlock> _owners = [];
    public InteractionBlock Blocked => _owners.Values.Aggregate(InteractionBlock.None, (a, b) => a | b);
    public IDisposable Acquire(InteractionBlock blocked)
    {
        var lease = new Lease(this);
        _owners.Add(lease, blocked);
        return lease;
    }
    private sealed class Lease(InteractionLocks owner) : IDisposable
    {
        public void Dispose() => owner._owners.Remove(this);
    }
}
