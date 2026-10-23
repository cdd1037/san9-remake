using Godot;

namespace San9.Client.Godot;

/// <summary>Opening catalog chrome, composed around one body rectangle with native transparent margins.</summary>
public static class OpeningWindowPainter
{
    public static void Draw(CanvasItem canvas, TextureScope textures, Rect2 body)
    {
        Texture2D T(string role) => textures.Get("opening-" + role, canvas);
        void Tile(string role, Rect2 rect) => SkinPainter.Tile(canvas, T(role), rect);
        void Strip(string role, Vector2 p, float width) => SkinPainter.Strip(canvas, T(role + "-left"),
            T(role + "-middle"), T(role + "-right"), new(p, new(width, T(role + "-left").GetHeight())));
        canvas.DrawTextureRectRegion(T("background"), body, new(Vector2.Zero, body.Size));
        float x = body.Position.X, y = body.Position.Y, right = body.End.X, bottom = body.End.Y;
        Tile("top", new(x + 64, y - 8, body.Size.X - 304, 16));
        Tile("bottom", new(x + 208, bottom - 16, body.Size.X - 448, 24));
        if (body.Size.Y > 432)
        {
            Tile("left", new(x - 16, y + 216, 24, body.Size.Y - 432));
            Tile("right", new(right - 8, y + 216, 16, body.Size.Y - 432));
        }
        canvas.DrawTexture(T("frame-top-left"), new(x - 56, y - 16));
        canvas.DrawTexture(T("frame-top-right"), new(right - 240, y - 16));
        canvas.DrawTexture(T("frame-bottom-left"), new(x - 56, bottom - 216));
        canvas.DrawTexture(T("frame-bottom-right"), new(right - 240, bottom - 216));
        Strip("title", new(x - 40, y - 16), 264);
        Strip("footer", new(right - 324, bottom - 64), 348);
    }
}
