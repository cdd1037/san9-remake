using Godot;

namespace San9.Client.Godot;

public partial class WorldEditorButton : BaseButton
{
    public TextureScope Textures { get; init; } = null!;
    public string Style { get; init; } = "field";
    public string Title { get; init; } = "";
    public bool Selected { get; init; }
    public int FontSize { get; init; } = 16;
    public HorizontalAlignment TextAlignment { get; init; } = HorizontalAlignment.Center;
    private readonly GameTextPresenter _text = new();
    private (bool, bool, bool)? _visual;
    public override void _Ready()
    {
        ClipContents = true;
        FocusMode = FocusModeEnum.All;
        var label = new Label { Text = Title, Size = Size - new Vector2(4,0), HorizontalAlignment = TextAlignment,
            VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = FontSize });
    }
    public override void _Process(double delta)
    {
        var next = (Disabled, IsHovered() || HasFocus(), GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed);
        if (_visual == next) return;
        _visual = next; QueueRedraw();
    }
    public override void _Draw()
    {
        if (Style.Length == 0) return;
        // The fourth source frame is a disabled option; selected toggles use the pressed frame.
        int frame = Disabled ? 3 : Selected || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed ? 2 : IsHovered() || HasFocus() ? 1 : 0;
        string suffix = frame == 0 ? "" : $"-{frame}";
        Texture2D Part(string p) => Textures.Get($"editor-{Style}-{p}" + suffix, this);
        SkinPainter.Strip(this, Part("left"), Part("middle"), Part("right"), new(Vector2.Zero, Size));
    }
    public override void _ExitTree() => _text.Dispose();
}
