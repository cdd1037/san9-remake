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
    public Action OpenCompanionSettings { get; init; } = null!;
    public Func<bool> CanConfigureCompanion { get; init; } = null!;
    public Action OpenExit { get; init; } = null!;
    public Action ReturnToOpening { get; init; } = null!;
    public Action<WorldEditorKind> OpenEditor { get; init; } = null!;
    public Action<WorldEditorKind> OpenSpecialEditor { get; init; } = null!;
    public Action OpenOfficerAppearance { get; init; } = null!;
    public int PendingAppearances { get; init; }
    public Func<WorldEditorKind, bool> CanEdit { get; init; } = null!;
    protected override Vector2 MenuSize => new(120, 392);
    protected override bool AlignRight => true;
    public override void _Ready()
    {
        Size = GetViewportRect().Size; MouseFilter = MouseFilterEnum.Stop;
        (string Title, string Role)[] allEntries = [("储存", "save"), ("载入", "load"),
            ("编辑武将", "edit-officers"), ("编辑军团", "edit-corps"), ("编辑都市", "edit-cities"),
            ("编辑设施", "edit-facilities"), ("编辑势力", "edit-forces"), ("编辑装备", "edit-equipment"),
            ("编辑障碍物", "edit-obstacles"), ("武将登场", "officer-appearance"),
            ("优化伴侣", "companion"), ("PK设定", "pk"),
            ("设定环境", "environment"), ("初始设定", "initial"), ("结束游戏", "exit")];
        var entries = allEntries;
        for (int i = 0; i < entries.Length; i++)
        {
            Action? action = entries[i].Role switch
            {
                "save" => () => Open(SaveLoadMode.Save),
                "load" => () => Open(SaveLoadMode.Load),
                "environment" => OpenEnvironment,
                "pk" => OpenPkSettings,
                "companion" when CanConfigureCompanion() => OpenCompanionSettings,
                "exit" => OpenExit,
                "initial" => ReturnToOpening,
                "edit-officers" when CanEdit(WorldEditorKind.Officers) => () => OpenEditor(WorldEditorKind.Officers),
                "edit-corps" when CanEdit(WorldEditorKind.Corps) => () => OpenEditor(WorldEditorKind.Corps),
                "edit-cities" when CanEdit(WorldEditorKind.Cities) => () => OpenEditor(WorldEditorKind.Cities),
                "edit-facilities" when CanEdit(WorldEditorKind.Facilities) => () => OpenEditor(WorldEditorKind.Facilities),
                "edit-forces" when CanEdit(WorldEditorKind.Forces) => () => OpenSpecialEditor(WorldEditorKind.Forces),
                "edit-equipment" when CanEdit(WorldEditorKind.Equipment) => () => OpenSpecialEditor(WorldEditorKind.Equipment),
                "edit-obstacles" when CanEdit(WorldEditorKind.Obstacles) => () => OpenEditor(WorldEditorKind.Obstacles),
                "officer-appearance" when CanConfigureCompanion() => OpenOfficerAppearance,
                _ => null
            };
            var button = new InformationMenuButton { Title = entries[i].Role == "officer-appearance" && PendingAppearances > 0 ? "登场待处理" : entries[i].Title, Textures = Textures,
                HintText = entries[i].Role is "edit-forces" or "edit-equipment" ? "编辑当前势力或装备，决定后统一保存。" :
                    entries[i].Role == "officer-appearance" ? PendingAppearances > 0 ? $"有{PendingAppearances}名武将的登场安排待处理。" : "设置未登场或已死亡武将的登场时间、地点及身份。" :
                    entries[i].Role == "companion" ? "设置辅助功能、势力、成长与战斗规则。快捷键：Tab。" :
                    Texts.ResolveRich(StrategyHintText.Key($"functions-{entries[i].Role}")),
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
