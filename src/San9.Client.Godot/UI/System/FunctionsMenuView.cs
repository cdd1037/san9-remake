using Godot;
using San9.Client.Godot.Persistence;
using San9.Content.Text;

namespace San9.Client.Godot;

public partial class FunctionsMenuView : Control
{
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public InformationMenuButton? HoverButton => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered());
    public Action<SaveLoadMode> Open { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action OpenEnvironment { get; init; } = null!;
    private static readonly Rect2 Bounds = new(904, 47, 120, 296);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        (string Title, string Role)[] entries = [("储存", "save"), ("载入", "load"),
            ("编辑武将", "edit-officers"), ("编辑军团", "edit-corps"), ("编辑都市", "edit-cities"),
            ("编辑设施", "edit-facilities"), ("编辑障碍物", "edit-obstacles"), ("PK设定", "pk"),
            ("设定环境", "environment"), ("初始设定", "initial"), ("结束游戏", "exit")];
        for (int i = 0; i < entries.Length; i++)
        {
            Action? action = entries[i].Role switch
            {
                "save" => () => Open(SaveLoadMode.Save),
                "load" => () => Open(SaveLoadMode.Load),
                "environment" => OpenEnvironment,
                _ => null
            };
            var button = new InformationMenuButton { Title = entries[i].Title, Textures = Textures,
                HintText = Texts.ResolveRich(StrategyHintText.Key($"functions-{entries[i].Role}")),
                Position = new(920, 63 + i * 24), Size = new(88, 24), Disabled = action is null };
            if (action is not null) button.Pressed += action;
            AddChild(button);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } &&
            !Bounds.HasPoint(GetLocalMousePosition())) Closed();
        AcceptEvent();
    }
    public override void _Draw()
    {
        SkinPainter.Tile(this, Textures["information-menu-center"], Bounds);
        SkinPainter.Tile(this, Textures["information-menu-top"], new(904, 47, 120, 16));
        SkinPainter.Tile(this, Textures["information-menu-bottom"], new(904, 327, 120, 16));
        SkinPainter.Tile(this, Textures["information-menu-left"], new(904, 47, 16, 296));
        SkinPainter.Tile(this, Textures["information-menu-right"], new(1008, 47, 16, 296));
        DrawTexture(Textures["information-menu-top-left"], new(904, 47));
        DrawTexture(Textures["information-menu-top-right"], new(1008, 47));
        DrawTexture(Textures["information-menu-bottom-left"], new(904, 327));
        DrawTexture(Textures["information-menu-bottom-right"], new(992, 311));
    }
}
