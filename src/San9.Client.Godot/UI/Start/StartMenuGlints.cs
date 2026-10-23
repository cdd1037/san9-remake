using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class StartMenuGlints : Control
{
    public TextureScope Textures { get; init; } = null!;
    public StartMenuAnimation[] Animations { get; set; } = [];
    public double Elapsed { get; set; }
    private readonly CanvasItemMaterial _additive = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    public override void _Ready() => Material = _additive;
    public override void _Draw()
    {
        if (!Textures.IsReady) { Textures.BindRedraw(this); return; }
        foreach (var animation in Animations) StartMenuArtwork.PaintAnimation(this, Textures, animation, Elapsed);
    }
    public override void _ExitTree() => Textures.Service.Defer(_additive.Dispose);
}
