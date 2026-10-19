using San9.Content.Text;
using Godot;

namespace San9.Client.Godot;

/// <summary>Shared arrival estimate plate used by command preparation pages.</summary>
public partial class CommandArrivalField : Control
{
    public TextureScope Textures { get; init; } = null!;
    public string Text { get => _label is null ? _value : _label.Text; set { _value = value; if (_label is not null) _label.Text = value; } }
    public static string FormatDays(TextCatalog texts, long? days) => days is long value ? CommandText.Resolve(texts, "value.arrival", value) : CommandText.Resolve(texts, "value.arrival-empty");
    private string _value = "";
    private Label? _label;
    private StyleBoxTexture _field = null!;
    private readonly GameTextPresenter _text = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _field = CommandPanelArt.Field(Textures, this);
        _label = new Label { Text = _value, Size = Size, ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        _text.Apply(_label, new(16, new Color("f0f0f0"), GameTextEffect.Shadow, Colors.Black));
    }

    public override void _Draw()
    {
        DrawStyleBox(_field, new(Vector2.Zero, Size));
        Rim(0); Rim(Size.Y - 4);
    }

    private void Rim(float y)
    {
        var left = Textures.Get("deployment-arrival-rim-left", this);
        var middle = Textures.Get("deployment-arrival-rim-middle", this);
        var right = Textures.Get("deployment-arrival-rim-right", this);
        SkinPainter.Tile(this, middle, new(left.GetWidth(), y, Size.X - left.GetWidth() - right.GetWidth() - 2, 4));
        DrawTextureRectRegion(left, new(0, y, left.GetWidth(), 4), new(0, 0, left.GetWidth(), 4));
        DrawTextureRectRegion(right, new(Size.X - right.GetWidth() - 2, y, right.GetWidth(), 4), new(0, 0, right.GetWidth(), 4));
    }

    public override void _ExitTree() { _field.Dispose(); _text.Dispose(); }
}
