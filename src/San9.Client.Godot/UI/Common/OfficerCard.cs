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
            AddChild(new OfficerAbilityRadar { Abilities = officer.Abilities, Textures = Textures,
                Layout = OfficerAbilityRadarLayout.Card, Position = new(107,43) });
        }
        QueueRedraw();
    }

    public override void _Draw() => DrawTexture(Textures.Get(Officer is null ? "detail-leader-background-1" : "detail-leader-background", this), Vector2.Zero);
    public override void _ExitTree() => _text.Dispose();
}

internal static class CommandPanelArt
{
    public static StyleBoxTexture Field(TextureScope textures, CanvasItem canvas) => new()
    {
        Texture = textures.Get("deployment-field", canvas), TextureMarginLeft = 16, TextureMarginRight = 16,
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

    public static void Strip(CanvasItem canvas, TextureScope textures, string role, Rect2 bounds)
    {
        var left = textures.Get(role+"-left", canvas);
        var right = textures.Get(role+"-right", canvas);
        if (role == "deployment-mode")
        {
            // Mode label endcaps contain eight transparent pixels outside the visible bounds.
            float l = left.GetWidth()-8, r = right.GetWidth()-8;
            canvas.DrawTextureRectRegion(left, new(bounds.Position, new(l,bounds.Size.Y)), new(8,0,l,left.GetHeight()));
            SkinPainter.Tile(canvas, textures.Get(role+"-middle", canvas), new(bounds.Position+new Vector2(l,0), new(bounds.Size.X-l-r,bounds.Size.Y)));
            canvas.DrawTextureRectRegion(right, new(new Vector2(bounds.End.X-r,bounds.Position.Y), new(r,bounds.Size.Y)), new(0,0,r,right.GetHeight()));
            return;
        }
        SkinPainter.Strip(canvas, left, textures.Get(role+"-middle", canvas), right, bounds);
    }

    public static void ColumnRule(CanvasItem canvas, TextureScope textures, Vector2 position, float height)
    {
        var top = textures.Get("deployment-column-top", canvas);
        var middle = textures.Get("deployment-column-middle", canvas);
        var bottom = textures.Get("deployment-column-bottom", canvas);
        canvas.DrawTexture(top, position);
        SkinPainter.Tile(canvas, middle, new(position+new Vector2(0,top.GetHeight()), new(2,height-top.GetHeight()-bottom.GetHeight())));
        canvas.DrawTexture(bottom, position+new Vector2(0,height-bottom.GetHeight()));
    }
}
