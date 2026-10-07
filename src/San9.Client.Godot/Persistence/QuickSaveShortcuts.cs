using Godot;
using San9.Application;
using San9.Application.Persistence;

namespace San9.Client.Godot.Persistence;

/// <summary>Global shortcuts and feedback; scene resources outlive the session being replaced.</summary>
public partial class QuickSaveShortcuts : Label
{
    public const string SavePath = "user://saves/quick.save.json";
    public Func<GameSession> CurrentSession { get; init; } = null!;
    public Func<bool> CanSave { get; init; } = null!;
    public Func<bool> IsBlocked { get; init; } = () => false;
    public Action LoadRequested { get; init; } = null!;
    private bool _pending;
    private double _remaining;

    public override void _Ready()
    {
        Position = new(16, 84); ZIndex = 1000; MouseFilter = MouseFilterEnum.Ignore; Visible = false;
        AddThemeColorOverride("font_color", Colors.White);
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 6);
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true } key || key.Keycode is not (Key.F1 or Key.F2) ||
            key.AltPressed || key.CtrlPressed || key.MetaPressed || key.ShiftPressed || IsBlocked()) return;
        GetViewport().SetInputAsHandled();
        if (key.Echo || _pending) return;
        _pending = true;
        bool save = key.Keycode == Key.F1;
        // Replacing controls must happen outside input dispatch.
        Callable.From(() => Execute(save)).CallDeferred();
    }

    private void Execute(bool save)
    {
        try
        {
            if (IsBlocked()) return;
            string path = ProjectSettings.GlobalizePath(SavePath);
            if (save)
            {
                if (!CanSave()) { Notify("当前不能快速存档，请结束交互并返回战略面。"); return; }
                SaveService.Write(path, CurrentSession());
                Notify("快速存档完成（F2 读取）");
            }
            else
            {
                if (!CanSave()) { Notify("当前不能快速读档，请结束交互并返回战略面。"); return; }
                LoadRequested();
            }
        }
        catch (SaveException error)
        {
            Notify($"快速存档失败：{SaveFailureMessages.Describe(error)}");
            GD.PushWarning(error.ToString());
        }
        finally { _pending = false; }
    }

    private void Notify(string message)
    {
        Text = message; Visible = true; _remaining = 5;
        GD.Print(message);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _remaining -= delta;
        if (_remaining <= 0) Hide();
    }
}
