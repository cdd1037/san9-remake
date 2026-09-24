using System.Runtime.InteropServices;
using Godot;

namespace San9.Client.Godot;

internal static class WindowStartup
{
    public static void Apply(Window window)
    {
        string[] arguments = OS.GetCmdlineArgs().TakeWhile(argument => argument != "--").ToArray();
        if (DisplayServer.GetName() == "headless" || window.Mode != Window.ModeEnum.Windowed ||
            arguments.Any(argument => argument is "--resolution" or "--embedded" or "--wid"))
            return;

        int windowId = window.GetWindowId();
        double scale;
        if (OperatingSystem.IsWindows())
        {
            // Godot's ScreenGetScale is not implemented on Windows. Query the
            // effective DPI of this window, not the monitor's physical density.
            var handle = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
            uint dpi = GetDpiForWindow(handle);
            if (dpi == 0)
                throw new InvalidOperationException("Cannot determine the startup window DPI.");
            scale = dpi / 96.0;
        }
        else
        {
            scale = DisplayServer.ScreenGetScale();
        }

        Rect2I usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
        Vector2I decorations = DisplayServer.WindowGetSizeWithDecorations(windowId) - window.Size;
        if (usable.Size.X <= decorations.X || usable.Size.Y <= decorations.Y)
            return;

        scale = Math.Min(scale, Math.Min(
            (double)(usable.Size.X - decorations.X) / ClientLayout.LogicalWidth,
            (double)(usable.Size.Y - decorations.Y) / ClientLayout.LogicalHeight));
        window.Size = new Vector2I(
            Math.Max(1, (int)Math.Floor(ClientLayout.LogicalWidth * scale)),
            Math.Max(1, (int)Math.Floor(ClientLayout.LogicalHeight * scale)));

        if (!arguments.Contains("--position"))
        {
            Vector2I clientOffset = window.Position - DisplayServer.WindowGetPositionWithDecorations(windowId);
            window.Position = usable.Position +
                (usable.Size - DisplayServer.WindowGetSizeWithDecorations(windowId)) / 2 + clientOffset;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);
}
