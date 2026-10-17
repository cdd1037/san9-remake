using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class SeasonChangeView : Control
{
    public TextureScope Textures { get; init; } = null!;
    private readonly CanvasItemMaterial _additive = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private TextureRect _cover = null!;
    private Label _error = null!;
    public Action RetryRequested { get; init; } = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 70;
        _cover = new TextureRect { Material = _additive, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        AddChild(_cover);
        _cover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _error = new Label { MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Hide();
    }

    public void Sample(SeasonChangeAnimation? animation, string? error = null)
    {
        Visible = animation is not null;
        if (animation is null) return;
        _error.Text = error is null ? "" : $"季节地图加载失败：{error}\n点击重试";
        _cover.Texture = Textures[$"season-cover-{animation.CoverFrame}"];
        _cover.SelfModulate = new Color(1, 1, 1, animation.CoverAlpha);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_error.Text.Length > 0 && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) RetryRequested();
        AcceptEvent();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Visible && input is InputEventKey or InputEventMouseButton or InputEventMouseMotion)
            GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _additive.Dispose();
}
