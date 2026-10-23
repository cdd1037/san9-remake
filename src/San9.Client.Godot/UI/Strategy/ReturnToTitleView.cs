using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class ReturnToTitleView : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public EndingPresentationDefinition Definition { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action? NewGame { get; init; }
    public Action LoadGame { get; init; } = null!;
    private double _elapsed;
    private bool _title;
    private double _titleTime;
    private Control? _menu;
    private TextureScope _sceneTextures = null!;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _sceneTextures = Service.CreateScope(Service.LibraryAssets);
        _sceneTextures.Request(Definition.TitleAssets);
    }
    public override void _Process(double delta)
    {
        if (!_sceneTextures.IsReady) return;
        if (_title) { _titleTime += delta; QueueRedraw(); return; }
        _elapsed += delta;
        if (_elapsed < Definition.LogoSeconds) { QueueRedraw(); return; }
        _title = true; QueueRedraw();
    }
    private void OpenMenu()
    {
        if (!_title || _menu is not null) return;
        var options = new List<(string Text, Action Action)>();
        if (NewGame is not null) options.Add(("开始新游戏", NewGame));
        options.Add(("载入游戏", LoadGame)); options.Add(("结束", () => GetTree().Quit()));
        _menu = new VerticalChoiceDialog { Textures = Textures, Options = options };
        AddChild(_menu);
    }
    public override void _Draw()
    {
        if (_sceneTextures.TryGetReady(_title ? Definition.Title : Definition.Logo, this, out var texture)) DrawTexture(texture, Vector2.Zero);
        if (!_title) return;
        foreach (var layer in Definition.TitleAnimations)
        {
            var sheet = _sceneTextures.Get(layer.Sprite.Asset, this);
            float width = sheet.GetWidth() / layer.Columns;
            int frame = (int)(_titleTime / layer.FrameSeconds) % layer.Columns;
            DrawTextureRectRegion(sheet, new(layer.Sprite.X, layer.Sprite.Y, width, sheet.GetHeight()), new(frame * width, 0, width, sheet.GetHeight()), new(1, 1, 1, layer.Sprite.Opacity));
        }
        var copyright = Definition.Copyright;
        DrawTexture(_sceneTextures.Get(copyright.Asset, this), new(copyright.X, copyright.Y), new(1, 1, 1, copyright.Opacity));
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) OpenMenu();
        AcceptEvent();
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.Space })
        { GetViewport().SetInputAsHandled(); OpenMenu(); }
    }
    public override void _ExitTree() => _sceneTextures.Dispose();
}
