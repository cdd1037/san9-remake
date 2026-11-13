using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Explicit whole-team choices, independent of the hovered member card.</summary>
public partial class DeploymentTacticMenu : ModalView
{
    public Control AnchorControl { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<DeploymentTacticMode> Selected { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private Rect2 _menuBounds;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        GlobalPosition = Vector2.Zero;
        Size = GetViewportRect().Size;
        var menuSize = new Vector2(160, 176);
        var anchor = GetGlobalTransform().AffineInverse() * AnchorControl.GlobalPosition;
        _menuBounds = new(new Vector2(Math.Clamp(anchor.X, 0, Math.Max(0, Size.X - menuSize.X)),
            Math.Clamp(anchor.Y + AnchorControl.Size.Y, 0, Math.Max(0, Size.Y - menuSize.Y))), menuSize);
        int index = 0;
        foreach (var mode in Enum.GetValues<DeploymentTacticMode>())
        {
            var button = new InformationMenuButton
            {
                Title = mode switch
                {
                    DeploymentTacticMode.Formation => "按当前阵形",
                    DeploymentTacticMode.Infantry => "步兵",
                    DeploymentTacticMode.Cavalry => "骑兵",
                    DeploymentTacticMode.MountedArchery => "弓骑",
                    DeploymentTacticMode.Crossbow => "弓弩",
                    DeploymentTacticMode.Intelligence => "智略",
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                }, Textures = Textures,
                Position = _menuBounds.Position + new Vector2(16, 16 + index++ * 24), Size = new(128, 24),
                TooltipText = mode == DeploymentTacticMode.Formation
                    ? "优先选择当前陆阵的优势类别；没有优势候选时选择其他适配兵法。"
                    : mode == DeploymentTacticMode.Intelligence
                        ? "智略默认偏好：幻术、妖术、治疗、攻心、教谕、陷阱、混乱、破陷、鼓舞、辱骂。"
                        : "仅选择指定类别且适配当前陆阵的兵法；无候选时保留原配置。"
            };
            button.Pressed += () => Selected(mode);
            AddChild(button);
        }
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } &&
            !_menuBounds.HasPoint(GetLocalMousePosition())) Closed();
        AcceptEvent();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }

    public override void _Draw() => SkinPainter.InformationMenu(this, Textures, _menuBounds);
}
