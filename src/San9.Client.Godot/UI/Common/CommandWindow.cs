using Godot;

namespace San9.Client.Godot;

/// <summary>Command chrome anchored to the caller’s content rectangle. Content and command state belong to the caller.</summary>
public partial class CommandWindow : Control
{
    public TextureScope Textures { get; init; } = null!;
    public string Title { get; init; } = "";
    public Action Help { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action Accepted { get; init; } = null!;
    public bool CanAccept { get; init; } = true;
    private readonly GameTextPresenter _text = new();
    private ImageTexture? _background;
    private Vector2 _backgroundSize;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var title = new Label { Text = Title, Position = new(16, -60), Size = new(Title.Length * 24 + 8, 32),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(title);
        _text.Apply(title, new(24, new Color("f8f850"), GameTextEffect.Outline, new Color("5828c8")));
        Icon("help", new(Size.X - 40, -56), Help);
        Icon("close", new(Size.X - 8, -36), Closed);
        var accept = ActionButton("执行", "accept", new(Size.X - 210, Size.Y + 20, 80, 48), Accepted);
        accept.Disabled = !CanAccept;
        ActionButton("中止", "cancel", new(Size.X - 122, Size.Y + 20, 80, 48), Closed);
    }

    public InformationMenuButton ActionButton(string title, string skin, Rect2 bounds, Action action)
    {
        var button = new InformationMenuButton { Title = title, SkinPrefix = "deployment-" + skin, Textures = Textures,
            Position = bounds.Position, Size = bounds.Size,
            // The edit skin reserves transparent space beyond its visible button plate.
            TextInsets = skin == "edit" ? new(0, 0, 8, 6) : Vector4.Zero,
            TextStyle = (hovered, pressed, disabled) => new(skin == "edit" ? 16 : 24,
                new Color(disabled ? hovered ? "000000" : "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8"),
                GameTextEffect.Outline, new Color(disabled ? hovered ? "f0f0f0" : "000000" : hovered || pressed
                    ? skin == "edit" ? "800090" : skin == "cancel" ? "983810" : "005028" : "000000")) };
        button.Pressed += action; AddChild(button); return button;
    }

    private void Icon(string role, Vector2 at, Action action)
    {
        var button = new TextureButton { Position = at, TextureNormal = Textures["deployment-" + role],
            TextureHover = Textures["deployment-" + role + "-1"], TexturePressed = Textures["deployment-" + role + "-2"] };
        button.Pressed += action; AddChild(button);
    }

    public override void _Process(double delta)
    {
        // Derived from the current skin and content size; never persisted as content.
        if (_background is not null && _backgroundSize == Size) return;
        string[] roles = ["deployment-background", "deployment-cutout-top-left", "deployment-cutout-top-right", "deployment-cutout-bottom-left"];
        if (!Textures.AreReady(roles)) return;
        using var source = Textures[roles[0]].GetImage();
        source.Convert(Image.Format.Rgba8);
        byte[] sourcePixels = source.GetData();
        int width = (int)Size.X + 64, height = (int)Size.Y + 128;
        int sourceWidth = source.GetWidth(), sourceHeight = source.GetHeight();
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            sourcePixels.AsSpan(((y % sourceHeight) * sourceWidth + x % sourceWidth) * 4, 4)
                .CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
        foreach (var (role, at) in new (string, Vector2I)[] { (roles[1], Vector2I.Zero),
            (roles[2], new(width - 144, 0)), (roles[3], new(0, height - 160)) })
        {
            using var mask = Textures[role].GetImage();
            mask.Convert(Image.Format.Rgba8);
            byte[] maskPixels = mask.GetData();
            int maskWidth = mask.GetWidth(), maskHeight = mask.GetHeight();
            for (int y = 0; y < maskHeight; y++)
            for (int x = 0; x < maskWidth; x++)
                if (x + at.X >= 0 && x + at.X < width && y + at.Y >= 0 && y + at.Y < height &&
                    maskPixels[(y * maskWidth + x) * 4 + 3] != 0)
                    pixels[((y + at.Y) * width + x + at.X) * 4 + 3] = 0;
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        _background?.Dispose(); _background = ImageTexture.CreateFromImage(image); _backgroundSize = Size; QueueRedraw();
    }
    public override void _Draw()
    {
        DrawRect(new(-Position, new(1024, 768)), new Color(0, 0, 0, .35f));
        if (_background is not null) DrawTexture(_background, new(-32, -64));
        float titleWidth = 160 + Title.Length * 16;
        Strip("title", new(-24, -72, titleWidth, 56));
        Tile("top-edge", new(-24 + titleWidth, -72, Size.X - 112 - (-24 + titleWidth), Textures["deployment-top-edge"].GetHeight()));
        Strip("footer", new(Size.X - 298, Size.Y + 16, 296, 56));
        Tile("edge", new(118, Size.Y + 56, Size.X - 416, Textures["deployment-edge"].GetHeight()));
        Tile("left-edge", new(-36, 104, Textures["deployment-left-edge"].GetWidth(), Size.Y - 136));
        Tile("right-edge", new(Size.X + 25, 88, Textures["deployment-right-edge"].GetWidth(), Size.Y - 120));
        foreach (var (role, at) in new (string, Vector2)[] { ("top-left", new(-62, -79)), ("top-right", new(Size.X - 112, -80)),
            ("bottom-left", new(-64, Size.Y - 96)), ("bottom-right", new(Size.X - 24, Size.Y - 96)) })
            DrawTexture(Textures["deployment-" + role], at);
    }
    private void Tile(string role, Rect2 rect) => SkinPainter.Tile(this, Textures["deployment-" + role], rect);
    private void Strip(string role, Rect2 rect) => SkinPainter.Strip(this, Textures["deployment-" + role + "-left"],
        Textures["deployment-" + role + "-middle"], Textures["deployment-" + role + "-right"], rect);
    public override void _ExitTree() { _text.Dispose(); _background?.Dispose(); }
}
