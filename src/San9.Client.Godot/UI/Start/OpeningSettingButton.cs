using Godot;

namespace San9.Client.Godot;

/// <summary>Opening choices use separate selection art and text, without engine button chrome.</summary>
public partial class OpeningSettingButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string? Highlight { get; init; }
    public bool Selected { get; set; }
    public string Title { get; set; } = "";
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;
    private readonly GameTextPresenter _text = new();
    private Label _label = null!;
    private (bool Selected, bool Active, bool Pressed, string Title)? _previous;
    public override void _Ready()
    {
        _label = new Label { Size = Size, HorizontalAlignment = Alignment,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label); _Process(0);
    }
    public override void _Process(double delta)
    {
        bool pressed = IsPressed(), active = IsHovered() || HasFocus();
        var state = (Selected, active, pressed, Title);
        if (_previous == state) return;
        _previous = state; _label.Text = Title;
        _text.Apply(_label, OpeningSettingsTextStyles.Choice(active));
        _label.Position = pressed ? Vector2.One : Vector2.Zero;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (Selected && Highlight is { } asset && Textures.TryGetReady(asset, this, out var texture))
            DrawTexture(texture, Vector2.Zero);
    }
    public override void _ExitTree() => _text.Dispose();
}
