using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class InformationMenuButton : BaseButton
{
    public string SkinPrefix { get; init; } = "information-menu-button";
    public Vector4 TextInsets { get; init; }
    private string _title = "";
    public string Title
    {
        get => _title;
        set { _title = value; if (_label is not null) _label.Text = value; }
    }
    public string HintText { get; init; } = "";
    public string? RichTitle { get; init; }
    public string DisplayText => _richLabel?.GetParsedText() ?? Title;
    public Func<bool, bool, bool, GameTextStyle> TextStyle { get; init; } = GameTextStyles.InformationMenu;
    public TextureScope Textures { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private Label? _label;
    private GameRichTextLabel? _richLabel;
    private (bool Hovered, bool Pressed, bool Disabled)? _state;
    public int VisualFrame { get; private set; }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        if (RichTitle is not null)
        {
            _richLabel = new() { Textures = Textures, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_richLabel);
            Resized += LayoutRichTitle;
        }
        else
        {
            _label = new Label { Text = Title, Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_label);
            _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        RefreshVisual();
    }

    public override void _Process(double delta) => RefreshVisual();
    public void RefreshVisual()
    {
        bool pressed = !Disabled && GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var state = (IsHovered(), pressed, Disabled);
        if (_state == state) return;
        _state = state;
        var style = TextStyle(state.Item1, pressed, Disabled);
        var offset = pressed ? new Vector2(2, 2) : Vector2.Zero;
        if (_label is not null)
        {
            _text.Apply(_label, style);
            _label.OffsetLeft = TextInsets.X + offset.X;
            _label.OffsetTop = TextInsets.Y + offset.Y;
            _label.OffsetRight = -TextInsets.Z + offset.X;
            _label.OffsetBottom = -TextInsets.W + offset.Y;
        }
        if (_richLabel is not null)
        {
            _richLabel.Present("[center]" + RichTitle + "[/center]", style);
            LayoutRichTitle();
        }
        VisualFrame = ButtonVisualState.Frame(false, state.Item1, pressed, Disabled);
        QueueRedraw();
    }

    private void LayoutRichTitle()
    {
        if (_richLabel is null) return;
        bool pressed = _state?.Pressed == true;
        var offset = pressed ? new Vector2(2, 2) : Vector2.Zero;
        float height = _richLabel.GetThemeFont("normal_font").GetHeight(_richLabel.Style.FontSize);
        _richLabel.Position = new Vector2(TextInsets.X, (Size.Y - height + TextInsets.Y - TextInsets.W) / 2) + offset;
        _richLabel.Size = new(Size.X - TextInsets.X - TextInsets.Z, height);
    }

    public override void _Draw()
    {
        string suffix = VisualFrame == 0 ? "" : $"-{VisualFrame}";
        var left = Textures.Get(SkinPrefix + "-left" + suffix, this);
        var middle = Textures.Get(SkinPrefix + "-middle" + suffix, this);
        var right = Textures.Get(SkinPrefix + "-right" + suffix, this);
        DrawTexture(left, Vector2.Zero);
        float end = Size.X - right.GetWidth();
        for (float x = left.GetWidth(); x < end; x += middle.GetWidth())
        {
            var size = new Vector2(Math.Min(middle.GetWidth(), end - x), middle.GetHeight());
            DrawTextureRectRegion(middle, new(new(x, 0), size), new(Vector2.Zero, size));
        }
        DrawTexture(right, new(end, 0));
    }

    public override void _ExitTree() { _text.Dispose(); }
}
