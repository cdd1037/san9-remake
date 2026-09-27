using Godot;

namespace San9.Client.Godot;

public sealed record OfficerProperty(string Title, string Value, int ValueWidth = 64, Color? TextColor = null);

/// <summary>Context-specific officer properties, such as politics, loyalty or affiliation.</summary>
public partial class OfficerPropertyList : Control
{
    public TextureScope Textures { get; init; } = null!;
    public int LabelWidth { get; init; } = 48;
    public int RowSpacing { get; init; } = 32;
    private OfficerProperty[] _properties = [];
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _field = null!;

    public void SetProperties(IEnumerable<OfficerProperty> properties)
    {
        var snapshot = properties.ToArray();
        if (snapshot.Any(p => p.ValueWidth < 32)) throw new ArgumentException("Property values need at least 32 pixels.", nameof(properties));
        _properties = snapshot;
        if (IsNodeReady()) Rebuild();
    }

    public override void _Ready()
    {
        if (LabelWidth < 32 || RowSpacing < 32) throw new InvalidOperationException("Property labels and row spacing must be at least 32 pixels.");
        MouseFilter = MouseFilterEnum.Ignore;
        _field = CommandPanelArt.Field(Textures);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        Size = new(LabelWidth+8+_properties.Select(p => p.ValueWidth).DefaultIfEmpty(0).Max(), _properties.Length*RowSpacing);
        for (int row = 0; row < _properties.Length; row++)
        {
            var property = _properties[row];
            CommandPanelArt.Label(this, _text, property.Title, new(0,row*RowSpacing,LabelWidth,24));
            CommandPanelArt.Label(this, _text, property.Value, new(LabelWidth+8,row*RowSpacing,property.ValueWidth,24), property.TextColor);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (int row = 0; row < _properties.Length; row++)
        {
            float y = row*RowSpacing;
            int width = _properties[row].ValueWidth;
            CommandPanelArt.Strip(this, Textures, "detail-admin-label", new(0,y,LabelWidth,24));
            DrawStyleBox(_field, new(LabelWidth+8,y,width,24));
            SkinPainter.Tile(this, Textures["deployment-field-rim"], new(LabelWidth+8,y,width,4));
            SkinPainter.Tile(this, Textures["deployment-field-rim"], new(LabelWidth+8,y+20,width,4));
        }
    }

    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
