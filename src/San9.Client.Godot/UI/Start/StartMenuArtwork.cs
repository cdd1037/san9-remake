using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class StartMenuArtwork : Control
{
    public StartMenuPresentation Definition { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private StartMenuEntry _entry = null!;
    private double _elapsed;
    private double _illustrationTime;
    private StartMenuGlints _glints = null!;
    private bool _menuVisible = true;
    public bool MenuVisible
    {
        get => _menuVisible;
        set { _menuVisible = value; _glints.Visible = value; QueueRedraw(); }
    }
    public override void _Ready()
    {
        _entry = Definition.Entries[0];
        _glints = new StartMenuGlints { Textures = Textures, Animations = _entry.Glints,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_glints);
    }
    public void Select(StartMenuEntry entry)
    {
        if (_entry == entry) return;
        _entry = entry; _illustrationTime = 0; _glints.Animations = entry.Glints; QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (!Textures.IsReady) return;
        _elapsed += delta; _illustrationTime += delta;
        _glints.Elapsed = _elapsed; _glints.QueueRedraw(); QueueRedraw();
    }
    public override void _Draw()
    {
        if (!Textures.IsReady) { Textures.BindRedraw(this); return; }
        foreach (var sprite in Definition.Background) DrawSprite(sprite);
        foreach (var animation in Definition.Ambience) PaintAnimation(this, Textures, animation, _elapsed);
        if (!MenuVisible) return;
        foreach (var sprite in Definition.Foreground) DrawSprite(sprite);
        PaintAnimation(this, Textures, _entry.Illustration, _illustrationTime);
    }
    private void DrawSprite(StartMenuSprite sprite) => DrawTexture(Textures.Get(sprite.Asset, this), new(sprite.X, sprite.Y));
    internal static void PaintAnimation(Control canvas, TextureScope textures, StartMenuAnimation animation, double elapsed)
    {
        int frame = (int)(elapsed / animation.FrameSeconds % (animation.Frames.Length * animation.Columns));
        var texture = textures.Get(animation.Frames[frame / animation.Columns], canvas);
        float width = texture.GetWidth() / animation.Columns;
        canvas.DrawTextureRectRegion(texture, new(animation.X, animation.Y, width, texture.GetHeight()),
            new(frame % animation.Columns * width, 0, width, texture.GetHeight()), new(1, 1, 1, animation.Opacity));
    }
}
