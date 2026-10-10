using Godot;

namespace San9.Client.Godot;

/// <summary>Native vertical choice family: 24px prompt lines, 16px gap and 40px option steps.</summary>
public partial class VerticalChoiceDialog : Control
{
    public TextureScope Textures { get; init; } = null!;
    public string Message { get; init; } = "";
    public IReadOnlyList<(string Text, Action Choose)> Options { get; init; } = [];
    public IReadOnlySet<int> DisabledOptions { get; init; } = new HashSet<int>();
    private StyleBoxTexture _frame = null!;
    private Rect2 _bounds;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _frame = new()
        {
            Texture = Textures["dialog-frame"],
            TextureMarginLeft = 96,
            TextureMarginRight = 96,
            TextureMarginTop = 56,
            TextureMarginBottom = 56,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile
        };
        var text = new GameRichTextLabel { Textures = Textures, FitContent = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(text); text.Present(Message, new(24, Colors.Black, GameTextEffect.None, Colors.Transparent));
        float width = Math.Clamp(Math.Max(384, Options.Select(o => o.Text.Length * 24 + 112).DefaultIfEmpty(384).Max()), 384, 960);
        text.Size = new(width - 112, 0); text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        float height = Math.Max(48, text.GetMinimumSize().Y) + 16 + 40 * Options.Count + 80;
        _bounds = new((1024 - width) / 2, (768 - height) / 2, width, height);
        text.Position = _bounds.Position + new Vector2(56, 40);
        float top = _bounds.Position.Y + height - 40 - 40 * Options.Count;
        for (int i = 0; i < Options.Count; i++)
        {
            var option = Options[i];
            var button = new InformationMenuButton
            {
                Textures = Textures,
                SkinPrefix = "decision-choice",
                Title = option.Text, Disabled = DisabledOptions.Contains(i),
                Position = new(_bounds.Position.X + 56, top + 40 * i),
                Size = new(width - 112, 32)
            };
            button.Pressed += option.Choose; AddChild(button);
        }
    }
    public override void _Draw() => DrawStyleBox(_frame, _bounds);
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _frame.Dispose();
}
