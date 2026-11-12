using Godot;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>Applies borderless fullscreen while retaining the current desktop window geometry.</summary>
internal sealed class WindowDisplay : IDisposable
{
    private readonly Window _window;
    private Vector2I _position;
    private Vector2I _size;
    private Window.ModeEnum _mode = Window.ModeEnum.Windowed;

    public WindowDisplay(Window window)
    {
        _window = window;
        _position = window.Position;
        _size = window.Size;
    }

    private bool IsDesktop => DisplayServer.GetName() != "headless" && !_window.IsEmbedded();

    public WindowPlacement? Capture()
    {
        if (!IsDesktop) return null;
        if (_window.Mode == Window.ModeEnum.Windowed)
        {
            _position = _window.Position;
            _size = _window.Size;
            _mode = Window.ModeEnum.Windowed;
        }
        else if (_window.Mode == Window.ModeEnum.Maximized) _mode = Window.ModeEnum.Maximized;
        return new(_position.X, _position.Y, _size.X, _size.Y, _mode == Window.ModeEnum.Maximized);
    }

    public void Restore(WindowPlacement placement)
    {
        if (!IsDesktop) return;
        placement.Validate();
        var position = new Vector2I(placement.X, placement.Y);
        var size = new Vector2I(placement.Width, placement.Height);
        int screen = DisplayServer.GetScreenFromRect(new Rect2I(position, size));
        if (screen < 0) screen = _window.CurrentScreen;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var decorations = DisplayServer.WindowGetSizeWithDecorations(_window.GetWindowId()) - _window.Size;
        var offset = _window.Position - DisplayServer.WindowGetPositionWithDecorations(_window.GetWindowId());
        size = size.Clamp(Vector2I.One, (usable.Size - decorations).Max(Vector2I.One));
        position = position.Clamp(usable.Position + offset, usable.End - size - decorations + offset);
        _window.Mode = Window.ModeEnum.Windowed;
        _window.Size = _size = size;
        _window.Position = _position = position;
        _window.Mode = _mode = placement.Maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed;
    }

    public bool HandleInput(InputEvent input)
    {
        if (!IsDesktop || !_window.HasFocus() || input is not InputEventKey { Keycode: Key.F9 } key) return false;
        if (key.Pressed && !key.Echo)
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Confined
                ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Confined;
        return true;
    }

    public void ReleaseMouse()
    {
        if (IsDesktop && Input.MouseMode == Input.MouseModeEnum.Confined)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Dispose()
    {
        ReleaseMouse();
    }

    public void Apply(bool fullscreen, bool mouseConfined)
    {
        if (!IsDesktop) return;
        var window = _window;
        bool isFullscreen = window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;
        if (fullscreen)
        {
            if (!isFullscreen)
            {
                Capture();
            }
            // Fullscreen covers the monitor without decorations or a display-mode change.
            window.Mode = Window.ModeEnum.Fullscreen;
        }
        else if (isFullscreen)
        {
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = _size;
            window.Position = _position;
            window.Mode = _mode;
        }
        Input.MouseMode = mouseConfined ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
    }
}
