using Godot;

namespace San9.Client.Godot;

public partial class NumberInputSlider : global::Godot.Range
{
    public Texture2D Fill { get; init; } = null!;
    public Texture2D Thumb { get; init; } = null!;
    private bool _dragging;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ValueChanged += _ => QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            _dragging = button.Pressed;
            if (_dragging) SetFromPointer(button.Position.X);
            AcceptEvent();
        }
        else if (_dragging && input is InputEventMouseMotion motion)
        {
            SetFromPointer(motion.Position.X);
            AcceptEvent();
        }
    }

    private void SetFromPointer(float x) => Value = MinValue + Math.Clamp(x / Fill.GetWidth(), 0, 1) * (MaxValue - MinValue);

    public override void _Draw()
    {
        float width = (float)Math.Floor(Fill.GetWidth() * (MaxValue == MinValue ? 1 : Ratio));
        if (width > 0) DrawTextureRectRegion(Fill, new(0, 0, width, Fill.GetHeight()), new(0, 0, width, Fill.GetHeight()));
        DrawTexture(Thumb, new(width - Thumb.GetWidth() / 2f, Fill.GetHeight()));
    }
}
