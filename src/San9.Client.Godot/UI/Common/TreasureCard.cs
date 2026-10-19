using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Reusable treasure name, image, traits and description, including the empty selection state.</summary>
public partial class TreasureCard : Control
{
    public TreasureDetailData? Treasure { get; init; }
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _field = null!;
    private static readonly Rect2[] Fields = [new(0,0,112,32),new(200,4,48,24),new(256,4,80,24),new(200,36,72,24),new(280,36,48,24),new(144,80,192,96)];
    public override void _Ready()
    {
        Size = new(336,176); MouseFilter = MouseFilterEnum.Ignore;
        _field = CommandPanelArt.Field(Textures, this);
        CommandPanelArt.Label(this,_text,Treasure?.Name ?? "",Fields[0]);
        CommandPanelArt.Label(this,_text,"种类",new(144,4,48,24));
        CommandPanelArt.Label(this,_text,"能力",new(144,36,48,24));
        string[] values = [Treasure?.Category ?? "", Treasure?.Effect ?? "", Treasure?.Ability ?? "", Treasure?.Tactic ?? ""];
        for (int i=0;i<values.Length;i++) CommandPanelArt.Label(this,_text,values[i],Fields[i+1]);
        if (Treasure is not { } treasure) return;
        AddChild(new AsyncPortrait { Service = Service, Role = treasure.PortraitRole, Position = new(0,40), Size = new(120,120),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        var description = new GameRichTextLabel { Textures = Textures, Position = new(156,92), Size = new(168,72) };
        AddChild(description); description.Present(treasure.Description,GameTextStyles.CatalogRow);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
    public override void _Draw()
    {
        foreach (var bounds in Fields)
        {
            DrawStyleBox(_field,bounds);
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(bounds.Position,new(bounds.Size.X,4)));
            SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(bounds.Position+new Vector2(0,bounds.Size.Y-4),new(bounds.Size.X,4)));
        }
        CommandPanelArt.Strip(this,Textures,"deployment-mode",new(144,4,48,24));
        CommandPanelArt.Strip(this,Textures,"deployment-mode",new(144,36,48,24));
    }
    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
