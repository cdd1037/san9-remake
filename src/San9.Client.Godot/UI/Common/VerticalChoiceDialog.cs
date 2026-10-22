using Godot;

namespace San9.Client.Godot;

/// <summary>Native vertical choice family: 24px prompt lines, 16px gap and 40px option steps.</summary>
public partial class VerticalChoiceDialog : FixedPage
{
    public TextureScope Textures { get; init; } = null!;
    public string Message { get; init; } = "";
    public IReadOnlyList<(string Text, Action Choose)> Options { get; init; } = [];
    public bool RichOptions { get; init; }
    public IReadOnlySet<int> DisabledOptions { get; init; } = new HashSet<int>();
    private StyleBoxTexture _frame = null!;
    private Rect2 _bounds;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _frame = new()
        {
            Texture = Textures.Get("dialog-frame", this),
            TextureMarginLeft = 96,
            TextureMarginRight = 96,
            TextureMarginTop = 56,
            TextureMarginBottom = 56,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile
        };
        bool hasPrompt = !string.IsNullOrEmpty(Message);
        var buttons = Options.Select((option, index) =>
        {
            var button = new InformationMenuButton
            {
                Textures = Textures, SkinPrefix = "decision-choice", Title = RichOptions ? "" : option.Text,
                RichTitle = RichOptions ? option.Text : null, Disabled = DisabledOptions.Contains(index),
                TextStyle = (hovered, pressed, disabled) => GameTextStyles.InformationMenu(hovered, pressed, disabled)
                    with { FontSize = 24 }
            };
            button.Pressed += option.Choose; AddChild(button);
            return button;
        }).ToArray();
        float inset = hasPrompt ? 56 : 96;
        var font = GetThemeFont("font");
        float optionWidth = buttons.Select(b => font.GetStringSize(b.DisplayText, fontSize: 24).X).DefaultIfEmpty(0).Max();
        // The native no-prompt family centers the option content, then expands the frame by 96/56.
        float width = hasPrompt ? Math.Clamp(Math.Max(384, optionWidth + 112), 384, 960)
            : Math.Clamp(12 * MathF.Ceiling(optionWidth / 12) + 96, 96, 792);
        GameRichTextLabel? text = null;
        if (hasPrompt)
        {
            text = new() { Textures = Textures, FitContent = true, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(text); text.Present(Message, new(24, Colors.Black, GameTextEffect.None, Colors.Transparent));
            text.Size = new(width - 112, 0); text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        float height = hasPrompt ? Math.Max(48, text!.GetMinimumSize().Y) + 16 + 40 * Options.Count + 80
            : 40 * Options.Count - 8;
        _bounds = new((1024 - width) / 2, (768 - height) / 2, width, height);
        if (text is not null) text.Position = _bounds.Position + new Vector2(56, 40);
        var optionOrigin = hasPrompt ? new Vector2(_bounds.Position.X + inset, _bounds.Position.Y + height - 40 - 40 * Options.Count)
            : _bounds.Position;
        float buttonWidth = hasPrompt ? width - 2 * inset : width;
        if (!hasPrompt)
            _bounds = new(_bounds.Position - new Vector2(96, 56),
                new Vector2(192 + 24 * MathF.Ceiling(width / 24), 112 + 24 * MathF.Ceiling(height / 24)));
        for (int i = 0; i < Options.Count; i++)
        {
            buttons[i].Position = optionOrigin + new Vector2(0, 40 * i);
            buttons[i].Size = new(buttonWidth, 32);
        }
    }
    public override void _Draw() => DrawStyleBox(_frame, _bounds);
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _frame.Dispose();
}
