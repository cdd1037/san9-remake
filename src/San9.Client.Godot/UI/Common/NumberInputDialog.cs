using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class NumberInputDialog : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public BoundedNumberInput State { get; init; } = null!;
    public string Subject { get; init; } = "士兵";
    public TextureScope Textures { get; init; } = null!;
    public Action<uint> Confirmed { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private Label _value = null!, _summary = null!;
    private NumberInputSlider _slider = null!;
    private BaseButton _confirm = null!;
    private uint _initial;
    private bool _refreshing;
    private bool _help;
    private static readonly Vector2 Origin = new(308, 108);
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _initial = State.Value;
        Label("输入数字", new(2, 3), new(152, 40), 24, GameTextEffect.Shadow);
        _summary = Label("", new(76, 57), new(256, 24));
        _value = Label("", new(157, 147), new(92, 32), 24);
        Label(State.Minimum.ToString(), new(50, 151), new(64, 24), alignment: HorizontalAlignment.Right);
        Label(State.Maximum.ToString(), new(292, 151), new(64, 24), alignment: HorizontalAlignment.Right);
        Button("最小", new(48, 187), new(64, 40), () => State.Set(State.Minimum), "number-min");
        Button("最大", new(298, 187), new(64, 40), () => State.Set(State.Maximum), "number-max");
        Button("", new(118, 195), new(24, 24), () => State.Step(-1), "number-minus");
        Button("", new(266, 195), new(24, 24), () => State.Step(1), "number-plus");
        _slider = new NumberInputSlider { Textures = Textures, Position = Origin + new Vector2(144, 203), Size = new(120, 32), MinValue = State.Minimum, MaxValue = State.Maximum, Step = 1, FocusMode = FocusModeEnum.None };
        _slider.ValueChanged += value => { if (!_refreshing) { State.Set((uint)value); Refresh(); } }; AddChild(_slider);
        Button("后退", new(107, 248), new(96, 46), State.Backspace, "number-back");
        Button("消去", new(203, 248), new(96, 46), State.Clear, "number-clear");
        string[] keys = ["7", "8", "9", "4", "5", "6", "1", "2", "3"];
        for (int i = 0; i < keys.Length; i++)
        { string digit = keys[i]; Button("", new(107 + i % 3 * 64, 292 + i / 3 * 48), new(64, 48), () => State.Append(digit), "number-" + digit); }
        Button("", new(107, 436), new(96, 48), () => State.Append("0"), "number-0");
        Button("", new(203, 436), new(96, 48), () => State.Append("00"), "number-00");
        _confirm = Button("执行", new(223, 491), new(72, 40), () => { if (State.CanConfirm) Confirmed(State.Value); }, "number-accept");
        Button("中止", new(303, 491), new(72, 40), Closed, "number-cancel");
        Button("", new(365, 22), new(40, 32), Closed, "number-close");
        Button("", new(334, 3), new(40, 32), () => _help = !_help, "number-help");
        AddChild(new GameMessageBar { Textures = Textures, Text = $"决定{Subject}。" }); Refresh();
    }
    private Label Label(string title, Vector2 position, Vector2 size, int fontSize = 16, GameTextEffect effect = GameTextEffect.Outline,
        HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label { Text = title, Position = Origin + position, Size = size, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = fontSize, Effect = effect }); return label;
    }
    private BaseButton Button(string title, Vector2 at, Vector2 size, Action action, string role)
    {
        var button = new NumberInputButton
        {
            Position = Origin + at,
            Size = size,
            Title = title, FontSize = role is "number-accept" or "number-cancel" ? 24 : 16,
            ActiveOutline = new Color(role == "number-min" ? "5828c8" : role == "number-accept" ? "005028" : role == "number-cancel" ? "983810" : "b82000"),
            StretchMode = TextureButton.StretchModeEnum.Keep,
            TextureNormal = Textures.Get("deployment-" + role, this),
            TextureHover = Textures.Get("deployment-" + role + "-1", this),
            TexturePressed = Textures.Get("deployment-" + role + "-2", this),
            TextureDisabled = Textures.Get("deployment-" + role + "-3", this)
        };
        button.Pressed += () => { action(); if (IsInsideTree()) Refresh(); }; AddChild(button);
        return button;
    }
    private void Refresh()
    { _value.Text = State.Value.ToString(); _summary.Text = _help ? "数字键输入，退格删除，回车决定。" : $"{Subject}：　{_initial} → {State.Value}"; _confirm.Disabled = !State.CanConfirm; _refreshing = true; _slider.Value = State.Value; _refreshing = false; }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        { GetViewport().SetInputAsHandled(); if (!mouse.Pressed) Closed(); return; }
        if (input is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Escape: GetViewport().SetInputAsHandled(); Closed(); return;
            case Key.Enter: case Key.KpEnter: GetViewport().SetInputAsHandled(); if (State.CanConfirm) Confirmed(State.Value); return;
            case Key.Backspace: State.Backspace(); break;
            case Key.Delete: State.Clear(); break;
            default:
                if (key.Unicode is >= '0' and <= '9') State.Append(((char)key.Unicode).ToString());
                else return;
                break;
        }
        GetViewport().SetInputAsHandled(); Refresh();
    }
    public override void _Draw()
    {
        DrawTexture(Textures.Get("deployment-number-background", this), Origin);
        var titleLeft = Textures.Get("deployment-number-title-left", this);
        var titleMiddle = Textures.Get("deployment-number-title-middle", this);
        var titleRight = Textures.Get("deployment-number-title-right", this);
        var titleAt = Origin + new Vector2(2, 3);
        DrawTexture(titleLeft, titleAt);
        DrawTexture(titleMiddle, titleAt + new Vector2(titleLeft.GetWidth(), 0));
        DrawTexture(titleRight, titleAt + new Vector2(152 - titleRight.GetWidth(), 0));
        var left = Textures.Get("deployment-number-field-left", this);
        var middle = Textures.Get("deployment-number-field-middle", this);
        var right = Textures.Get("deployment-number-field-right", this);
        var at = Origin + new Vector2(52, 51);
        DrawTexture(left, at);
        float end = 304 - right.GetWidth();
        for (float x = left.GetWidth(); x < end; x += middle.GetWidth())
        {
            var size = new Vector2(Math.Min(middle.GetWidth(), end - x), middle.GetHeight());
            DrawTextureRectRegion(middle, new(at + new Vector2(x, 0), size), new(Vector2.Zero, size));
        }
        DrawTexture(right, at + new Vector2(end, 0));
    }
    public override void _ExitTree() => _text.Dispose();
}
