using Godot;
namespace San9.Client.Godot;

public partial class ScrollThumbStyle : StyleBox
{
    public Texture2D Top { get; init; } = null!;
    public Texture2D Middle { get; init; } = null!;
    public Texture2D Bottom { get; init; } = null!;
    public override void _Draw(Rid canvasItem, Rect2 rect)
    {
        for (float y = 0; y < rect.Size.Y; y += Middle.GetHeight())
        {
            var size = new Vector2(rect.Size.X, Math.Min(Middle.GetHeight(), rect.Size.Y - y));
            Middle.DrawRectRegion(canvasItem, new(rect.Position + new Vector2(0, y), size), new(Vector2.Zero, size));
        }
        Top.DrawRectRegion(canvasItem, new(rect.Position, new Vector2(rect.Size.X, Top.GetHeight())), new(Vector2.Zero, new Vector2(rect.Size.X, Top.GetHeight())));
        Bottom.DrawRectRegion(canvasItem, new(rect.Position + new Vector2(0, rect.Size.Y - Bottom.GetHeight()), new Vector2(rect.Size.X, Bottom.GetHeight())), new(Vector2.Zero, new Vector2(rect.Size.X, Bottom.GetHeight())));
    }
}
