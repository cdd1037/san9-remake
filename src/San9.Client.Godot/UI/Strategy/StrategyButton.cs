using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyButton : TextureButton
{
    public Texture2D[] Frames { get; init; } = [];
    public bool Territory { get; init; }
    public GameButtonTextRole TextRole { get; init; }
    private readonly GameTextPresenter _text = new();
    private (bool Hovered, bool Selected, bool Disabled)? _lastState;
    public string Title { get; set; } = "";
    public string HintText { get; init; } = "";
    private Label _label = null!;
    public Vector2 TextPosition => _label.Position;
    public int VisualFrame { get; private set; }

    public override void _Ready()
    {
        _label = new Label { Text = Title, Size = Size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_label);
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseEntered += () => { if (!Disabled) GrabFocus(); };
        RefreshVisual();
    }
    public override void _Process(double delta) => RefreshVisual();

    public void RefreshVisual()
    {
        _label.Text = Title;
        bool selected = ButtonPressed || GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        bool highlighted = IsHovered() || HasFocus();
        var state = (highlighted, selected, Disabled);
        if (_lastState == state) return;
        _lastState = state;
        _text.Apply(_label, GameTextStyles.Button(TextRole, state.Item1, selected, Disabled));
        var offset = ButtonVisualState.TextOffset(selected);
        _label.Position = new(offset.X, offset.Y);
        VisualFrame = ButtonVisualState.Frame(Territory, highlighted, selected, Disabled);
        // Use one resolved texture for every Godot draw mode: the original has
        // distinct selected-hover and disabled-selected states.
        var texture = Frames[VisualFrame];
        TextureNormal = texture;
        TextureHover = texture;
        TexturePressed = texture;
        TextureDisabled = texture;
    }
    public LabelSettings TextSettings => _label.LabelSettings;
    public override void _ExitTree() => _text.Dispose();
}
