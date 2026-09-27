using Godot;

namespace San9.Client.Godot;

internal static class SkinPainter
{
    public static void Tile(CanvasItem canvas, Texture2D texture, Rect2 rect)
    {
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
        {
            var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
            canvas.DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size));
        }
    }
    public static void Strip(CanvasItem canvas, Texture2D left, Texture2D middle, Texture2D right, Rect2 rect)
    {
        Tile(canvas, middle, new(rect.Position + new Vector2(left.GetWidth(), 0),
            new(rect.Size.X - left.GetWidth() - right.GetWidth(), rect.Size.Y)));
        canvas.DrawTextureRect(left, new(rect.Position, new(left.GetWidth(), rect.Size.Y)), false);
        canvas.DrawTextureRect(right, new(new(rect.End.X - right.GetWidth(), rect.Position.Y), new(right.GetWidth(), rect.Size.Y)), false);
    }
}
