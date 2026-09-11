using Godot;

namespace San9.Client.Godot;

/// <summary>Reusable textured vertical scrollbar with a native range and native drag/page input.</summary>
public partial class SkinnedScrollBar : Control
{
    public ScrollBarSkin Skin { get; init; } = null!;
    public Action? ContextRequested { get; init; }
    public VScrollBar Range { get; private set; } = null!;
    public TextureButton Up { get; private set; } = null!;
    public TextureButton Down { get; private set; } = null!;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        Range = new ScrollRange { Scrolled = ScrollBy,
            Step = 1, CustomStep = 1, FocusMode = FocusModeEnum.All };
        Range.AddThemeStyleboxOverride("scroll", new StyleBoxEmpty());
        Range.AddThemeStyleboxOverride("scroll_focus", new StyleBoxEmpty());
        var thumb = Skin.CreateThumbStyle();
        foreach (string name in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
            Range.AddThemeStyleboxOverride(name, thumb);
        var empty = new ImageTexture();
        foreach (string name in new[] { "increment", "increment_highlight", "increment_pressed",
            "decrement", "decrement_highlight", "decrement_pressed" })
            Range.AddThemeIconOverride(name, empty);
        AddChild(Range);
        TextureButton Arrow(Texture2D[] frames, int step)
        {
            var button = new TextureButton { TextureNormal = frames[0], TextureHover = frames[1],
                TexturePressed = frames[2], TextureDisabled = frames[3], FocusMode = FocusModeEnum.All };
            button.Pressed += () => ScrollBy(step);
            button.MouseEntered += () => { if (!button.Disabled) button.GrabFocus(); };
            button.FocusEntered += () => button.TextureNormal = frames[1];
            button.FocusExited += () => button.TextureNormal = frames[0];
            button.GuiInput += ForwardInput;
            AddChild(button); return button;
        }
        Up = Arrow(Skin.UpFrames, -1); Down = Arrow(Skin.DownFrames, 1);
        Range.ValueChanged += _ => UpdateArrows();
        Range.MouseEntered += () => Range.GrabFocus();
        Range.GuiInput += ForwardInput;
        Resized += Layout;
        Layout();
    }

    private void Layout()
    {
        Range.Position = Skin.TrackInset;
        Range.Size = new(Skin.ThumbMiddle.GetWidth(), Size.Y - Skin.TrackInset.Y * 2);
        Up.Position = new(Skin.TrackInset.X, Skin.TrackInset.Y - Skin.UpFrames[0].GetHeight());
        Down.Position = new(Skin.TrackInset.X, Size.Y - Skin.TrackInset.Y);
        QueueRedraw();
    }

    private void ScrollBy(int direction) => Range.Value += Math.Sign(direction);

    public void UpdateArrows()
    {
        Up.Disabled = Range.Value <= Range.MinValue;
        Down.Disabled = Range.Value >= Range.MaxValue - Range.Page;
    }

    private void ForwardInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse) return;
        if (!mouse.Pressed && mouse.ButtonIndex == MouseButton.Right)
        { ContextRequested?.Invoke(); AcceptEvent(); }
        if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { ScrollBy(mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1); AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent @event) => ForwardInput(@event);

    public override void _Draw()
    {
        for (int y = 0; y < Size.Y; y += Skin.TrackMiddle.GetHeight())
        {
            float height = Math.Min(Skin.TrackMiddle.GetHeight(), Size.Y - y);
            DrawTextureRectRegion(Skin.TrackMiddle, new(0, y, Size.X, height), new(0, 0, Size.X, height));
        }
        // A skin may provide a full-height track overlay, so cap drawing must stay inside the viewport.
        float topHeight = Math.Min(Size.Y, Skin.TrackTop.GetHeight());
        DrawTextureRectRegion(Skin.TrackTop, new(0, 0, Size.X, topHeight), new(0, 0, Size.X, topHeight));
        float bottomHeight = Math.Min(Size.Y, Skin.TrackBottom.GetHeight());
        DrawTextureRectRegion(Skin.TrackBottom, new(0, Size.Y - bottomHeight, Size.X, bottomHeight),
            new(0, Skin.TrackBottom.GetHeight() - bottomHeight, Size.X, bottomHeight));
    }
}
