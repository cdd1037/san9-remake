using Godot;
using San9.Client.Godot.Persistence;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class SaveSlotFlagView : Control
{
    public TextureService Service { get; init; } = null!;
    public SaveSlotFlag Flag { get; init; } = null!;
    private TextureScope? _textures;
    private string[] _frames = [];

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new(68, 52);
        _frames = FlagVisuals.Layers(5, Flag.Color, Flag.Glyph)
            .Select(layer => Service.AnimationClips[layer].Frames[3]).ToArray();
        _textures = Service.CreateScope(Service.MapAssets, owner: this);
        _textures.Request(_frames);
    }

    public override void _Draw()
    {
        if (_textures is null) return;
        foreach (string frame in _frames) DrawTexture(_textures.Get(frame, this), Vector2.Zero);
    }

    public override void _ExitTree() => _textures?.Dispose();
}
