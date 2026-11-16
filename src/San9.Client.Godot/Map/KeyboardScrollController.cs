using Godot;

namespace San9.Client.Godot;

/// <summary>Only fresh, unhandled map key presses own a scrolling gesture.</summary>
internal sealed class KeyboardScrollController
{
    private readonly HashSet<Key> _held = [];

    public void Cancel() => _held.Clear();

    public void Release(Key key) => _held.Remove(key);

    public bool Press(Key key, bool echo, bool available)
    {
        if (!available) Cancel();
        if (!available || echo || key is not (Key.W or Key.A or Key.S or Key.D)) return false;
        _held.Add(key);
        return true;
    }

    public EdgeScrollDirection Direction(bool available)
    {
        if (!available) Cancel();
        return new((_held.Contains(Key.D) ? 1 : 0) - (_held.Contains(Key.A) ? 1 : 0),
            (_held.Contains(Key.S) ? 1 : 0) - (_held.Contains(Key.W) ? 1 : 0));
    }
}
