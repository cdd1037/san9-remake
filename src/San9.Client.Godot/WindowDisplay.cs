using Godot;

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
        window.FocusExited += ReleaseMouse;
    }

    private bool IsDesktop => DisplayServer.GetName() != "headless" && !_window.IsEmbedded();

    public bool HandleInput(InputEvent input)
    {
        if (!IsDesktop || !_window.HasFocus() || input is not InputEventKey { Keycode: Key.F9 } key) return false;
        if (key.Pressed && !key.Echo)
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Confined
                ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Confined;
        return true;
    }

    private void ReleaseMouse()
    {
        if (IsDesktop && Input.MouseMode == Input.MouseModeEnum.Confined)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Dispose()
    {
        _window.FocusExited -= ReleaseMouse;
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
                _position = window.Position;
                _size = window.Size;
                _mode = window.Mode == Window.ModeEnum.Maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed;
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
