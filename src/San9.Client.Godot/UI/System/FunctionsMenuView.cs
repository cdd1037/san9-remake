using Godot;
using San9.Client.Godot.Persistence;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class FunctionsMenuView : HudDropdown
{
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public InformationMenuButton? HoverButton => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered());
    public Action<SaveLoadMode> Open { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action OpenEnvironment { get; init; } = null!;
    public Action OpenPkSettings { get; init; } = null!;
    public Action OpenExit { get; init; } = null!;
    public Action<WorldEditorKind> OpenEditor { get; init; } = null!;
    public Func<WorldEditorKind, bool> CanEdit { get; init; } = null!;
    protected override Vector2 MenuSize => new(120, 296);
    protected override bool AlignRight => true;
    public override void _Ready()
    {
        Size = GetViewportRect().Size; MouseFilter = MouseFilterEnum.Stop;
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
                "pk" => OpenPkSettings,
                "exit" => OpenExit,
                "edit-officers" when CanEdit(WorldEditorKind.Officers) => () => OpenEditor(WorldEditorKind.Officers),
                "edit-corps" when CanEdit(WorldEditorKind.Corps) => () => OpenEditor(WorldEditorKind.Corps),
                "edit-cities" when CanEdit(WorldEditorKind.Cities) => () => OpenEditor(WorldEditorKind.Cities),
                "edit-facilities" when CanEdit(WorldEditorKind.Facilities) => () => OpenEditor(WorldEditorKind.Facilities),
                "edit-obstacles" when CanEdit(WorldEditorKind.Obstacles) => () => OpenEditor(WorldEditorKind.Obstacles),
                _ => null
            };
            var button = new InformationMenuButton { Title = entries[i].Title, Textures = Textures,
                HintText = Texts.ResolveRich(StrategyHintText.Key($"functions-{entries[i].Role}")),
                Position = new(16, 16 + i * 24), Size = new(88, 24), Disabled = action is null };
            if (action is not null) button.Pressed += action;
            AddChild(button);
        }
        _Process(0);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } &&
            !MenuBounds.HasPoint(GetLocalMousePosition())) Closed();
        AcceptEvent();
    }
    public override void _Draw() => DrawFrame(Textures);
}
