using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Edits local military intentions; command submission is a separate use case.</summary>
public partial class MilitaryPolicyView : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
    public MilitaryTargets Selection { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Func<bool, CommandRejection> Submit { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private bool IsConstruction => Selection is SpecialDeploymentTargets { IsConstruction: true };
    private bool IsSpecial => Selection is SpecialDeploymentTargets or UnitCommandTargets { Purpose: not UnitPurpose.Combat };
    private Vector2 Origin => new(304, IsSpecial ? IsConstruction ? 304 : 320 : 224);
    private CommandWindow _window = null!;
    private StyleBoxTexture _field = null!;
    private Control? _child;
    private Label _delay = null!;
    private CommandArrivalField _arrival = null!;
    private GameMessageBar _message = null!;
    private float _facilityWidth;
    private bool _confirmAttack;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _field = new StyleBoxTexture { Texture = Textures.Get("deployment-field", this),
            TextureMarginLeft = 16, TextureMarginRight = 16, TextureMarginTop = 8, TextureMarginBottom = 8,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        if (IsSpecial) { BuildSpecial(); return; }
        _window = new CommandWindow { Texts = Texts, Textures = Textures, Position = Origin, Size = new(416, 320),
            Title = "决定方针", ShowBehindParent = true, Closed = Closed, Accepted = Execute,
            Help = () => Notice("决定部队的行动方针。金色为已选项。\n" +
                (Selection.IsDeployment ? "出征时期可设为0至9日后。\n" : "") +
                "到达预定按当前目标、中继点及阻挡预测行军时间。") };
        AddChild(_window);
        string facility = Selection.Source.IsUnit
            ? "命令部队　" + UnitMapInteraction.Hover(Selection.View, Texts, Selection.Source.Id)?.Name
            : "命令设施　" + Selection.View.Settlements.Single(s => s.Id == Selection.Source.Id).ResolveName(Texts);
        _facilityWidth = facility.Sum(c => c < 128 ? 8 : 16) + 32;
        Label(facility, new(0, 0, _facilityWidth, 32));
        AddArrival();
        RefreshArrival();
        var target = Selection.Selected!.Value;
        var info = target.IsUnit ? UnitMapInteraction.Hover(Selection.View, Texts, target.Id) : MapHoverInfo.Resolve(Selection.View, Texts, target.Id, MapInformationContext.Strategy);
        Label("命令", new(0, 48, 96, 32));
        Label(Selection.Candidates[target] == MilitaryMarker.Enter ? "进军" : "攻击", new(104, 48, 48, 32));
        Label("目标", new(0, 88, 96, 32)); Label(info?.Name ?? "—", new(104, 88, 160, 32));
        Label("势力", new(272, 92, 48, 24)); Label(info?.Force is { } force ? Texts.Resolve(force.NameKey) : "—", new(328, 92, 80, 24));
        Choice("敌接近时", "攻击", "无视", 128, Selection.Policies.AttackApproachingEnemies,
            (p, value) => p with { AttackApproachingEnemies = value });
        Choice("自主撤退", "许可", "不许可", 168, Selection.Policies.AutonomousRetreat,
            (p, value) => p with { AutonomousRetreat = value });
        Choice("追击", "许可", "不许可", 208, Selection.Policies.Pursue,
            (p, value) => p with { Pursue = value });
        Choice("事后命令", "攻击", "撤退", 248, Selection.Policies.AttackAfterAction,
            (p, value) => p with { AttackAfterAction = value });
        if (Selection.IsDeployment)
        {
            _window.ActionButton("出征时期", "edit", new(0, 288, 104, 40), SelectDelay);
            _delay = Label($"{Selection.DepartureDelayDays}日后", new(104, 288, 64, 32));
        }
        _message = new GameMessageBar { Textures = Textures, Text = Selection.Waypoint is { } via
            ? $"决定部队行动方针。中继点：({via.X}, {via.Y})。" : "决定部队行动方针。" };
        AddChild(_message);
    }
    private void BuildSpecial()
    {
        _window = new CommandWindow { Texts = Texts, Textures = Textures, Position = Origin, Size = new(416,IsConstruction ? 160 : 128),
            Title = "决定方针", ShowBehindParent = true, Closed = Closed, Accepted = Execute,
            Help = () => Notice("决定部队是否可以自主撤退。到达预定包含所选中继点。") };
        AddChild(_window);
        string home = Selection.Source.IsUnit
            ? "命令部队　" + UnitMapInteraction.Hover(Selection.View, Texts, Selection.Source.Id)?.Name
            : "命令设施　" + Selection.View.Settlements.Single(s => s.Id == Selection.Source.Id).ResolveName(Texts);
        _facilityWidth = home.Sum(c => c < 128 ? 8 : 16) + 32; Label(home,new(0,0,_facilityWidth,32));
        AddArrival(); RefreshArrival();
        var target = Selection.Selected!.Value;
        string name = IsConstruction ? Texts.Resolve(Selection.View.RegionNameKeys[target.Id]) :
            Selection.View.Settlements.Single(s => s.Id == target.Id).ResolveName(Texts);
        int y = 48;
        if (Selection is SpecialDeploymentTargets { IsConstruction: true } special)
        {
            Label("工作物",new(0,y,96,32)); Label(special.Draft.FacilityName(special.Draft.FacilityId!.Value,Texts),new(104,y,80,32)); y+=40;
        }
        Label("目标",new(0,y,96,32)); Label(name,new(104,y,160,32)); y+=40;
        Choice("自主撤退","许可","不许可",y,Selection.Policies.AutonomousRetreat,(p,value)=>p with { AutonomousRetreat=value });
        _message = new GameMessageBar { Textures=Textures,Text=Texts.ResolveRich(SpecialDeploymentText.Key("policy")) }; AddChild(_message);
    }
    private Label Label(string text, Rect2 bounds)
    {
        var label = new Label { Text = text, Position = Origin + bounds.Position, Size = bounds.Size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow); return label;
    }
    private void Choice(string caption, string yes, string no, int y, bool selected, Func<UnitPolicies, bool, UnitPolicies> change)
    {
        Label(caption, new(0, y, 96, 32));
        var group = new ButtonGroup { AllowUnpress = false };
        foreach (bool value in new[] { true, false })
        {
            var button = new CommandChoiceButton { Title = value ? yes : no, Textures = Textures,
                Position = Origin + new Vector2(value ? 104 : 192, y), Size = new(80, 32),
                ToggleMode = true, ButtonGroup = group, ButtonPressed = selected == value };
            button.Pressed += () => { Selection.SetPolicies(change(Selection.Policies, value)); RefreshArrival(); }; AddChild(button);
        }
    }
    private void RefreshArrival()
    {
        var estimate = Selection.EstimateArrival();
        string value = estimate.Status switch
        {
            ArrivalEstimateStatus.Known => $"{estimate.Days,3}日　",
            ArrivalEstimateStatus.Waiting => "受阻",
            ArrivalEstimateStatus.Unreachable => "不可达",
            ArrivalEstimateStatus.Incomplete => "待定",
            _ => "—"
        };
        _arrival.Text = $"到达预定　{value}";
    }
    private void AddArrival()
    {
        _arrival = new CommandArrivalField { Textures = Textures, Position = Origin + new Vector2(256,0), Size = new(136,32) };
        AddChild(_arrival);
    }
    private void Execute()
    {
        var rejection = Submit(_confirmAttack);
        if (rejection == CommandRejection.None) return;
        _confirmAttack = rejection == CommandRejection.AttackConfirmationRequired;
        _message.SetMessage(_confirmAttack ? "攻击该目标将影响外交关系。再次点击执行确认命令。" : "命令条件已变化，请返回重新选择。", true);
    }
    private void SelectDelay()
    {
        _child = new NumberInputDialog { State = new(0, 9, (uint)Selection.DepartureDelayDays), Subject = "出征时期",
            Textures = Textures, Closed = CloseChild,
            Confirmed = days => { Selection.SetDepartureDelay((int)days); _delay.Text = $"{days}日后"; RefreshArrival(); CloseChild(); } };
        AddChild(_child);
    }
    private void Notice(string text)
    {
        var dialog = new GameDialog { Textures = Textures, Size = new(1024, 768) };
        dialog.Closed += CloseChild; _child = dialog; AddChild(dialog); dialog.SetNotice(text);
    }
    private void CloseChild()
    { if (_child is not null) { RemoveChild(_child); _child.QueueFree(); _child = null; } }
    public override void _Input(InputEvent input)
    {
        if (_child is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed(); }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        { GetViewport().SetInputAsHandled(); if (mouse.Pressed) Closed(); }
    }
    public override void _Draw()
    {
        Strip("facility", new(0, 0, _facilityWidth, 32));
        if (IsSpecial)
        {
            int last = IsConstruction ? 128 : 88;
            for (int y=48;y<=last;y+=40) Strip("label",new(0,y,96,32));
            List<Rect2> fields = [new(104,IsConstruction?88:48,160,32)];
            if (IsConstruction) fields.Add(new(104,48,80,32));
            foreach (var bounds in fields) DrawField(bounds);
            DrawLine(Origin+new Vector2(0,40),Origin+new Vector2(416,40),new Color("755635"),2);
            return;
        }
        foreach (int y in new[] { 48, 88, 128, 168, 208, 248 }) Strip("label", new(0, y, 96, 32));
        CommandPanelArt.Strip(this, Textures, "deployment-mode", new(Origin+new Vector2(272,92),new Vector2(48,24)));
        foreach (var bounds in new Rect2[] { new(104, 48, 48, 32), new(104, 88, 160, 32),
            new(328, 92, 80, 24), new(104, 288, 64, 32) })
        {
            if (!Selection.IsDeployment && bounds.Position.Y == 288) continue;
            DrawField(bounds);
        }
        DrawLine(Origin + new Vector2(0, 40), Origin + new Vector2(416, 40), new Color("755635"), 2);
    }
    private void DrawField(Rect2 bounds)
    {
        var rect = new Rect2(Origin+bounds.Position,bounds.Size); DrawStyleBox(_field,rect);
        SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(rect.Position,new(rect.Size.X-2,4)));
        SkinPainter.Tile(this,Textures.Get("deployment-field-rim", this),new(rect.Position+new Vector2(0,rect.Size.Y-4),new(rect.Size.X-2,4)));
    }
    private void Strip(string role, Rect2 bounds) => SkinPainter.Strip(this, Textures.Get("deployment-" + role + "-left", this),
        Textures.Get("deployment-" + role + "-middle", this), Textures.Get("deployment-" + role + "-right", this), new(Origin + bounds.Position, bounds.Size));
    public override void _ExitTree() { _text.Dispose(); _field.Dispose(); }
}
