using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class StartMenuButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public StartMenuSprite Sprite { get; init; } = null!;
    public string Title { get; init; } = "";
    private readonly GameTextPresenter _text = new();
    private int _frame = -1;
    private Label _label = null!;
    public override void _Ready()
    {
        _label = new Label { Text = Title, Position = new(100, 0), Size = new(336, 36),
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        _text.Apply(_label, new(24, new Color("f8d860"), GameTextEffect.Outline, new Color("603000")));
        _Process(0);
    }
    public override void _Process(double delta)
    {
        int frame = Disabled ? 3 : IsPressed() ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        if (frame == _frame) return;
        _frame = frame; QueueRedraw();
    }
    public override void _Draw()
    {
        if (!Textures.TryGetReady(Sprite.Asset, this, out var texture)) return;
        float width = texture.GetWidth() / Sprite.Columns;
        DrawTextureRectRegion(texture, new(0, 0, width, texture.GetHeight()), new(Math.Max(0, _frame) * width, 0, width, texture.GetHeight()));
    }
    public override void _ExitTree() => _text.Dispose();
}
