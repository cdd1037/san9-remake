using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Edits local deployment intentions; command submission is a separate use case.</summary>
public partial class DeploymentPolicyView : Control
{
    public DeploymentTargets Selection { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Func<bool, CommandRejection> Submit { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    public override void _Ready()
    {
        Size = new(1024, 768); ZIndex = 30; MouseFilter = MouseFilterEnum.Stop;
        var center = new CenterContainer(); AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new(600, 440) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxTexture
        {
            Texture = Textures["dialog-frame"], TextureMarginLeft = 96, TextureMarginRight = 96,
            TextureMarginTop = 56, TextureMarginBottom = 56,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            ContentMarginLeft = 56, ContentMarginRight = 56, ContentMarginTop = 36, ContentMarginBottom = 36
        });
        center.AddChild(panel);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 16); panel.AddChild(column);
        void Label(string value, int size = 20)
        {
            var label = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Center };
            _text.Apply(label, new(size, Colors.Black, GameTextEffect.None, Colors.Transparent)); column.AddChild(label);
        }
        Label("方针设定", 24);
        var target = Selection.Selected!.Value;
        var info = target.IsUnit ? UnitMapInteraction.Hover(Selection.Draft.View, Texts, target.Id) : MapHoverInfo.Resolve(Selection.Draft.View, Texts, target.Id);
        Label("目标：" + info?.Name);
        if (Selection.Waypoint is { } via) Label($"中继点：({via.X}, {via.Y})", 16);
        var current = Selection.Policies;
        Toggle("攻击接近的敌军", current.AttackApproachingEnemies, (p, enabled) => p with { AttackApproachingEnemies = enabled });
        Toggle("追击敌军", current.Pursue, (p, enabled) => p with { Pursue = enabled });
        Toggle("任务结束后攻击", current.AttackAfterAction, (p, enabled) => p with { AttackAfterAction = enabled });
        Toggle("自主撤退", current.AutonomousRetreat, (p, enabled) => p with { AutonomousRetreat = enabled });
        var delayRow = new HBoxContainer(); column.AddChild(delayRow);
        var delayLabel = new Label { Text = "出征等待（日）", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _text.Apply(delayLabel, new(20, Colors.Black, GameTextEffect.None, Colors.Transparent)); delayRow.AddChild(delayLabel);
        var delay = new SpinBox { MinValue = 0, MaxValue = 9, Step = 1, Value = Selection.DepartureDelayDays, CustomMinimumSize = new(104, 40) };
        delay.ValueChanged += value => Selection.SetDepartureDelay((int)value); delayRow.AddChild(delay);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.Apply(error, new(16, Colors.Black, GameTextEffect.None, Colors.Transparent)); column.AddChild(error);
        var deploy = new InformationMenuButton { Title = "出征", SkinPrefix = "deployment-edit", Textures = Textures,
            CustomMinimumSize = new(104, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        bool confirmAttack = false;
        deploy.Pressed += () =>
        {
            var rejection = Submit(confirmAttack);
            if (rejection == CommandRejection.None) return;
            confirmAttack = rejection == CommandRejection.AttackConfirmationRequired;
            error.Text = confirmAttack ? "攻击该目标将影响外交关系。再次点击确认出征。" : "出征条件已变化，请返回重新选择。";
            deploy.Title = confirmAttack ? "确认出征" : "出征";
        };
        column.AddChild(deploy);
        var back = new InformationMenuButton { Title = "返回", SkinPrefix = "deployment-cancel", Textures = Textures,
            CustomMinimumSize = new(96, 40), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        back.Pressed += Closed; column.AddChild(back);

        void Toggle(string name, bool enabled, Func<UnitPolicies, bool, UnitPolicies> change)
        {
            var row = new HBoxContainer(); column.AddChild(row);
            var label = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _text.Apply(label, new(20, Colors.Black, GameTextEffect.None, Colors.Transparent)); row.AddChild(label);
            var button = new InformationMenuButton { Title = enabled ? "许可" : "禁止", SkinPrefix = "deployment-edit", Textures = Textures, CustomMinimumSize = new(104, 40) };
            button.Pressed += () => { enabled = !enabled; Selection.SetPolicies(change(Selection.Policies, enabled)); button.Title = enabled ? "许可" : "禁止"; button.QueueRedraw(); };
            row.AddChild(button);
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) { AcceptEvent(); Closed(); }
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() => _text.Dispose();
}
