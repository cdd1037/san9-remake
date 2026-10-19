using Godot;

namespace San9.Client.Godot;

public sealed record OfficerProperty(string Title, string Value, int ValueWidth = 64, Color? TextColor = null);

/// <summary>Context-specific officer properties, such as politics, loyalty or affiliation.</summary>
public partial class OfficerPropertyList : Control
{
    public TextureScope Textures { get; init; } = null!;
    public string LabelSkinPrefix { get; init; } = "detail-admin-label";
    public int LabelWidth { get; init; } = 48;
    public int ValueGap { get; init; } = 8;
    public int RowSpacing { get; init; } = 32;
    public int RowHeight { get; init; } = 24;
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
        if (LabelWidth < 32 || ValueGap < 0 || RowHeight < 24 || RowSpacing < RowHeight) throw new InvalidOperationException("Invalid property row dimensions.");
        MouseFilter = MouseFilterEnum.Ignore;
        _field = CommandPanelArt.Field(Textures, this);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        Size = new(LabelWidth+ValueGap+_properties.Select(p => p.ValueWidth).DefaultIfEmpty(0).Max(), _properties.Length*RowSpacing);
        for (int row = 0; row < _properties.Length; row++)
        {
            var property = _properties[row];
            CommandPanelArt.Label(this, _text, property.Title, new(0,row*RowSpacing,LabelWidth,RowHeight));
            CommandPanelArt.Label(this, _text, property.Value, new(LabelWidth+ValueGap,row*RowSpacing,property.ValueWidth,RowHeight), property.TextColor);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (int row = 0; row < _properties.Length; row++)
        {
            float y = row*RowSpacing;
            int width = _properties[row].ValueWidth;
            CommandPanelArt.Strip(this, Textures, LabelSkinPrefix, new(0,y,LabelWidth,RowHeight));
            DrawStyleBox(_field, new(LabelWidth+ValueGap,y,width,RowHeight));
            SkinPainter.Tile(this, Textures.Get("deployment-field-rim", this), new(LabelWidth+ValueGap,y,width,4));
            SkinPainter.Tile(this, Textures.Get("deployment-field-rim", this), new(LabelWidth+ValueGap,y+RowHeight-4,width,4));
        }
    }

    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
