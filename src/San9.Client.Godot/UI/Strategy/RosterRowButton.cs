using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>A momentary row whose highlight represents retained keyboard focus.</summary>
public partial class RosterRowButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public San9.Application.Presentation.RosterKey? EntryKey { get; private set; }
    public Action<int>? Scrolled { get; init; }
    public Action? ContextRequested { get; init; }
    private GameRichTextLabel _label = null!;
    public int VisualFrame { get; private set; }
    public Vector2 TextOffset => _label.Position;
    public Color TextColor => _label.Style.Foreground;

    public override void _Ready()
    {
        Size = new(100, 16); FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        _label = new GameRichTextLabel { Size = new(100, 16), Textures = Textures };
        AddChild(_label);
        MouseEntered += () => { if (!Disabled) GrabFocus(); };
    }

    public void Bind(San9.Application.Presentation.RosterEntry? entry, string title, Color color)
    {
        if (EntryKey != entry?.Key && HasFocus()) ReleaseFocus();
        EntryKey = entry?.Key; Visible = entry is not null; Disabled = entry is null;
        _label.Present(title,
            GameTextStyles.For(GameTextRole.Roster) with { Foreground = color });
        RefreshVisual();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        { ContextRequested?.Invoke(); AcceptEvent(); return; }
        if (@event is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { Scrolled?.Invoke(mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1); AcceptEvent(); }

        }
    }

    public override void _Input(InputEvent @event)
    {
        // Godot's spatial focus search is a different interaction from Tab traversal.
        if (HasFocus() && @event is InputEventKey { Keycode: Key.Up or Key.Down or Key.Left or Key.Right })
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta) => RefreshVisual();
    public void RefreshVisual()
    {
        bool pressed = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        VisualFrame = Disabled ? 3 : pressed ? 2 : HasFocus() ? 1 : 0;
        var offset = pressed ? new Vector2(2, 2) : Vector2.Zero;
        _label.Position = offset;
        QueueRedraw();
    }

    public override void _Draw()
    {
        string suffix = VisualFrame == 0 ? "" : $"-{VisualFrame}";
        var left = Textures.Get("roster-row-left" + suffix, this);
        var middle = Textures.Get("roster-row-middle" + suffix, this);
        var right = Textures.Get("roster-row-right" + suffix, this);
        DrawTexture(left, Vector2.Zero);
        for (int x = 8; x < 92; x += 8)
        {
            int width = Math.Min(8, 92 - x);
            DrawTextureRectRegion(middle, new(x, 0, width, 16), new(0, 0, width, 16));
        }
        DrawTexture(right, new(92, 0));
    }
}
