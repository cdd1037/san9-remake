using Godot;

namespace San9.Client.Godot;

/// <summary>Native delegation operation and policy choices share a titled, tiled panel.</summary>
public partial class CorpsChoiceDialog : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .45f);
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public IReadOnlyList<string> Options { get; init; } = [];
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlySet<int> DisabledOptions { get; init; } = new HashSet<int>();
    public Action<int> Selected { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private float PanelHeight => 72 + Options.Count * 60;
    private Vector2 Origin => new(362, (768 - PanelHeight) / 2 + 2);
    private Vector2 TitleOrigin => Origin-new Vector2(5,1);
    private readonly GameTextPresenter text = new();

    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        // Native title text starts at the frame's (32,8) inset; it is not centered in its 44px height.
        var title = new Label { Text = Title, Position = TitleOrigin + new Vector2(32,8), Size = new(96,24),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(title); text.Apply(title,GameTextStyles.CatalogRow with { FontSize = 24 });
        for (int i = 0; i < Options.Count; i++)
        {
            int choice = i;
            var button = new InformationMenuButton { Title = Options[i], SkinPrefix = "deployment-policy-choice", Textures = Textures,
                Position = Origin + new Vector2(72,47+i*60), Size = new(156,52), Disabled = DisabledOptions.Contains(i),
                TextInsets = new(0,4,0,0), TextStyle = (hovered,pressed,disabled) => new(24,
                    new Color(disabled ? "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
                    GameTextEffect.Outline, !disabled && (hovered || pressed) ? new Color("5828c8") : Colors.Black) };
            button.Pressed += () => Selected(choice); AddChild(button);
        }
        AddChild(new GameMessageBar { Textures = Textures, Text = Hint });
    }

    public override void _Draw()
    {
        DrawTextureRectRegion(Textures.Get("deployment-policy-background", this),new(Origin,new Vector2(296,PanelHeight)),new(Vector2.Zero,new Vector2(296,PanelHeight)));
        Vector2 frame = Origin-new Vector2(1,1);
        void Piece(string role, Vector2 at) => DrawTexture(Textures.Get("deployment-policy-frame-"+role, this),frame+at);
        Piece("top-left",Vector2.Zero); Piece("top-right",new(196,0));
        Piece("bottom-left",new(0,PanelHeight-96)); Piece("bottom-right",new(196,PanelHeight-96));
        for (int i = 0; i < 6; i++) { Piece("top",new(100+i*16,0)); Piece("bottom",new(100+i*16,PanelHeight-4)); }
        for (float y = 104; y < PanelHeight-96; y += 16)
        {
            float height = Math.Min(16, PanelHeight-96-y);
            foreach (var (role, x) in new[] { ("left", 0), ("right", 288) })
            {
                var texture = Textures.Get("deployment-policy-frame-"+role, this);
                var size = new Vector2(texture.GetWidth(),height);
                DrawTextureRectRegion(texture,new(frame+new Vector2(x,y),size),new(Vector2.Zero,size));
            }
        }
        Vector2 title = TitleOrigin;
        DrawTexture(Textures.Get("deployment-policy-title-left", this),title);
        for (int i = 0; i < 4; i++) DrawTexture(Textures.Get("deployment-policy-title-middle", this),title+new Vector2(32+i*24,0));
        DrawTexture(Textures.Get("deployment-policy-title-right", this),title+new Vector2(128,0));
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        { GetViewport().SetInputAsHandled(); if (!mouse.Pressed) Closed(); }
        else if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() => text.Dispose();
}
