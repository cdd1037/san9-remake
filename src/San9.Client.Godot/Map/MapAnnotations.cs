using Godot;
using San9.Content.Text;

namespace San9.Client.Godot;

// Keeps main-map annotations in the HUD canvas; drawing and state belong to its map view.
public partial class MapAnnotations : Node2D
{
    public MapWorld World { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Font UiFont { get; init; } = null!;
    private long _revision = -1;
    private long _annotationRevision = -1;
    private MapObjectTarget? _hovered;
    public long DrawCount { get; private set; }
    public override void _Ready() => World.Display!.ConfigureAnnotations(UiFont, Texts);
    public override void _Process(double delta)
    {
        long annotationRevision = World.Display?.AnnotationRevision ?? 0;
        if (_revision == World.ViewRevision && _annotationRevision == annotationRevision && _hovered == World.HoveredObject) return;
        _revision = World.ViewRevision; _annotationRevision = annotationRevision;
        _hovered = World.HoveredObject; QueueRedraw();
    }
    public override void _Draw()
    {
        DrawCount++;
        World.Display?.DrawAnnotations(this, UiFont, Texts, World.WorldToViewport, World.ZoomFactor);
    }
}
