using Godot;

namespace San9.Client.Godot;

internal static class SkinPainter
{
    public static void InformationMenu(CanvasItem canvas, TextureScope textures, Rect2 bounds)
    {
        var p = bounds.Position;
        var s = bounds.Size;
        Tile(canvas, textures["information-menu-center"], bounds);
        Tile(canvas, textures["information-menu-top"], new(p, new(s.X, 16)));
        Tile(canvas, textures["information-menu-bottom"], new(p + new Vector2(0, s.Y - 16), new(s.X, 16)));
        Tile(canvas, textures["information-menu-left"], new(p, new(16, s.Y)));
        Tile(canvas, textures["information-menu-right"], new(p + new Vector2(s.X - 16, 0), new(16, s.Y)));
        canvas.DrawTexture(textures["information-menu-top-left"], p);
        canvas.DrawTexture(textures["information-menu-top-right"], p + new Vector2(s.X - 16, 0));
        canvas.DrawTexture(textures["information-menu-bottom-left"], p + new Vector2(0, s.Y - 16));
        canvas.DrawTexture(textures["information-menu-bottom-right"], p + new Vector2(s.X - 32, s.Y - 32));
    }

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
