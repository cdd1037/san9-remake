using Godot;

namespace San9.Client.Godot;

/// <summary>Delegation policy popup uses the shared native tiled choice-panel art.</summary>
public partial class CorpsPolicyDialog : Control
{
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlySet<int> DisabledOptions { get; init; } = new HashSet<int>();
    public Action<int> Selected { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private static readonly Vector2 Origin = new(362,230);
    private static readonly Vector2 TitleOrigin = Origin-new Vector2(5,1);
    private readonly GameTextPresenter text = new();

    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        // Native title text starts at the frame's (32,8) inset; it is not centered in its 44px height.
        var title = new Label { Text = "委任方针", Position = TitleOrigin + new Vector2(32,8), Size = new(96,24),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(title); text.Apply(title,GameTextStyles.CatalogRow with { FontSize = 24 });
        string[] names = ["委任","重视内政","打倒敌势","攻略设施"];
        for (int i = 0; i < names.Length; i++)
        {
            int choice = i;
            var button = new InformationMenuButton { Title = names[i], SkinPrefix = "deployment-policy-choice", Textures = Textures,
                Position = Origin + new Vector2(72,47+i*60), Size = new(156,52), Disabled = DisabledOptions.Contains(i),
                TextInsets = new(0,4,0,0), TextStyle = (hovered,pressed,disabled) => new(24,
                    new Color(disabled ? "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
                    GameTextEffect.Outline, !disabled && (hovered || pressed) ? new Color("5828c8") : Colors.Black) };
            button.Pressed += () => Selected(choice); AddChild(button);
        }
        AddChild(new GameMessageBar { Textures = Textures, Text = "选择军团委任方针。" });
    }

    public override void _Draw()
    {
        DrawRect(new(0,0,1024,768),new Color(0,0,0,.45f));
        DrawTextureRectRegion(Textures["deployment-policy-background"],new(Origin,new Vector2(296,312)),new(Vector2.Zero,new Vector2(296,312)));
        Vector2 frame = Origin-new Vector2(1,1);
        void Piece(string role, Vector2 at) => DrawTexture(Textures["deployment-policy-frame-"+role],frame+at);
        Piece("top-left",Vector2.Zero); Piece("top-right",new(196,0));
        Piece("bottom-left",new(0,216)); Piece("bottom-right",new(196,216));
        for (int i = 0; i < 6; i++) { Piece("top",new(100+i*16,0)); Piece("bottom",new(100+i*16,308)); }
        for (int i = 0; i < 7; i++) { Piece("left",new(0,104+i*16)); Piece("right",new(288,104+i*16)); }
        Vector2 title = TitleOrigin;
        DrawTexture(Textures["deployment-policy-title-left"],title);
        for (int i = 0; i < 4; i++) DrawTexture(Textures["deployment-policy-title-middle"],title+new Vector2(32+i*24,0));
        DrawTexture(Textures["deployment-policy-title-right"],title+new Vector2(128,0));
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
