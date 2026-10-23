using Godot;

namespace San9.Client.Godot;

/// <summary>Catalog bulk actions have their own small gold button and text states.</summary>
public partial class CatalogBulkButton : BaseButton
{
    public string Title { get; init; } = "";
    public TextureScope Textures { get; init; } = null!;
    public Func<bool, bool, bool, GameTextStyle>? TextStyle { get; init; }
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool Hovered, bool Pressed, bool Disabled)? _previous;
    private int _frame;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        bool hovered = IsHovered();
        bool pressed = !Disabled && GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state = (hovered, pressed, Disabled);
        if (_previous == state) return;
        _previous = state;
        _frame = Disabled ? 3 : pressed ? 2 : hovered ? 1 : 0;
        _text.Apply(_label, TextStyle?.Invoke(hovered, pressed, Disabled) ?? GameTextStyles.CatalogBulk(hovered, pressed, Disabled));
        _label.Position = new Vector2(0, -1) + (pressed ? new Vector2(2, 2) : Vector2.Zero);
        QueueRedraw();
    }

    public override void _Draw()
    {
        string suffix = _frame == 0 ? "" : $"-{_frame}";
        var left = Textures.Get("catalog-bulk-left" + suffix, this);
        var middle = Textures.Get("catalog-bulk-middle" + suffix, this);
        var right = Textures.Get("catalog-bulk-right" + suffix, this);
        DrawTexture(left, Vector2.Zero);
        for (float x = left.GetWidth(); x < Size.X - right.GetWidth(); x += middle.GetWidth())
        {
            var size = new Vector2(Math.Min(middle.GetWidth(), Size.X - right.GetWidth() - x), middle.GetHeight());
            DrawTextureRectRegion(middle, new(new Vector2(x, 0), size), new(Vector2.Zero, size));
        }
        DrawTexture(right, new Vector2(Size.X - right.GetWidth(), 0));
    }

    public override void _ExitTree() => _text.Dispose();
}
