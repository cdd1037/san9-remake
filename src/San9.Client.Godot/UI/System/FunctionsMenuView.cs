using Godot;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot;

public partial class FunctionsMenuView : Control
{
    public TextureScope Textures { get; init; } = null!;
    public Action<SaveLoadMode> Open { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private static readonly Rect2 Bounds = new(904, 47, 120, 296);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        string[] entries = ["储存", "载入", "编辑武将", "编辑军团", "编辑都市", "编辑设施", "编辑障碍物", "PK设定", "设定环境", "初始设定", "结束游戏"];
        for (int i = 0; i < entries.Length; i++)
        {
            int index = i;
            var button = new InformationMenuButton { Title = entries[i], Textures = Textures,
                Position = new(920, 63 + i * 24), Size = new(88, 24), Disabled = i >= 2 };
            button.Pressed += () => Open(index == 0 ? SaveLoadMode.Save : SaveLoadMode.Load);
            AddChild(button);
        }
        AddChild(new GameMessageBar { Textures = Textures, Text = "选择储存或载入。" });
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
