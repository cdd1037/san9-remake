using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class StartMenuButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public StartMenuSprite Sprite { get; init; } = null!;
    public string Title { get; init; } = "";
    public GameButtonTextRole? TextRole { get; init; }
    public Func<bool, bool, bool, GameTextStyle>? TextStyle { get; init; }
    private readonly GameTextPresenter _text = new();
    private int _frame = -1;
    private (bool Active, bool Pressed, bool Disabled)? _previous;
    private Label _label = null!;
    public override void _Ready()
    {
        _label = new Label { Text = Title, Position = TextRole is null ? new(100, 0) : Vector2.Zero,
            Size = TextRole is null ? new(336, 36) : Size,
            HorizontalAlignment = TextRole is null ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        _text.Apply(_label, new(24, new Color("f8d860"), GameTextEffect.Outline, new Color("603000")));
        _Process(0);
    }
    public override void _Process(double delta)
    {
        bool active = IsHovered() || HasFocus(), pressed = IsPressed();
        var state = (active, pressed, Disabled);
        if (_previous == state) return;
        _previous = state;
        _frame = Disabled ? 3 : pressed ? 2 : active ? 1 : 0;
        if (TextStyle is { } style)
            _text.Apply(_label, style(active, pressed, Disabled));
        if (TextRole is { } role)
        {
            if (TextStyle is null)
                _text.Apply(_label, GameTextStyles.Button(role, active, pressed, Disabled));
            _label.Position = pressed ? Vector2.One : Vector2.Zero;
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (!Textures.TryGetReady(Sprite.Asset, this, out var texture)) return;
        float width = texture.GetWidth() / Sprite.Columns;
        DrawTextureRectRegion(texture, new(0, 0, width, texture.GetHeight()), new(Math.Max(0, _frame) * width, 0, width, texture.GetHeight()));
    }
    public override void _ExitTree() => _text.Dispose();
}
