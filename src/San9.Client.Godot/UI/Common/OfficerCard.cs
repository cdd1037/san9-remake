using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

public sealed record OfficerCardData(string Name, string PortraitRole, OfficerAbilities Abilities);

/// <summary>Compact officer portrait and abilities; null data selects the empty artwork.</summary>
public partial class OfficerCard : Control
{
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private OfficerCardData? _officer;
    public OfficerCardData? Officer
    {
        get => _officer;
        set { _officer = value; if (IsNodeReady()) Rebuild(); }
    }
    private readonly GameTextPresenter _text = new();

    public override void _Ready()
    {
        Size = new(200,120);
        MouseFilter = MouseFilterEnum.Ignore;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        if (Officer is { } officer)
        {
            CommandPanelArt.Label(this, _text, officer.Name, new(8,7,64,16));
            AddChild(new AsyncPortrait { Service = Service, Role = officer.PortraitRole,
                View = San9.Content.Images.PortraitView.Face, Position = new(7,28), Size = new(64,80), MouseFilter = MouseFilterEnum.Ignore });
            AddChild(new OfficerAbilityRadar { Abilities = officer.Abilities, Position = new(107,43), Scale = new(52f/164,52f/164) });
            AddChild(new TextureRect { Texture = Textures["detail-leader-radar-grid"], Position = new(107,43), MouseFilter = MouseFilterEnum.Ignore });
            CommandPanelArt.Label(this, _text, "统率", new(116,27,32,16), new Color("f8f850"));
            CommandPanelArt.Label(this, _text, "武力", new(76,60,32,16), new Color("e85440"));
            CommandPanelArt.Label(this, _text, "智力", new(116,94,32,16), new Color("60d868"));
            CommandPanelArt.Label(this, _text, "政治", new(162,60,32,16), new Color("5098d8"));
        }
        QueueRedraw();
    }

    public override void _Draw() => DrawTexture(Textures[Officer is null ? "detail-leader-background-1" : "detail-leader-background"], Vector2.Zero);
    public override void _ExitTree() => _text.Dispose();
}

internal static class CommandPanelArt
{
    public static StyleBoxTexture Field(TextureScope textures) => new()
    {
        Texture = textures["deployment-field"], TextureMarginLeft = 16, TextureMarginRight = 16,
        TextureMarginTop = 8, TextureMarginBottom = 8,
        AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile
    };

    public static void Label(Control parent, GameTextPresenter text, string value, Rect2 bounds, Color? color = null,
        HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label { Text = value, Position = bounds.Position, Size = bounds.Size,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(label);
        text.Apply(label, GameTextStyles.CatalogRow with { Foreground = color ?? new Color("f0f0f0") });
    }

    public static void Strip(CanvasItem canvas, TextureScope textures, string role, Rect2 bounds) =>
        SkinPainter.Strip(canvas, textures[role+"-left"], textures[role+"-middle"], textures[role+"-right"], bounds);

    public static void ColumnRule(CanvasItem canvas, TextureScope textures, Vector2 position, float height)
    {
        var top = textures["deployment-column-top"];
        var middle = textures["deployment-column-middle"];
        var bottom = textures["deployment-column-bottom"];
        canvas.DrawTexture(top, position);
        SkinPainter.Tile(canvas, middle, new(position+new Vector2(0,top.GetHeight()), new(2,height-top.GetHeight()-bottom.GetHeight())));
        canvas.DrawTexture(bottom, position+new Vector2(0,height-bottom.GetHeight()));
    }
}
