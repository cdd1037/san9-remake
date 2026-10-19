using Godot;

namespace San9.Client.Godot;

internal sealed class DetailSkinPainter(CanvasItem canvas, TextureScope textures)
{
    public void Tile(string role, Rect2 rect)
    {
        SkinPainter.Tile(canvas, textures.Get("detail-" + role, canvas), rect);
    }
    public void Strip(string role, Rect2 rect)
    {
        SkinPainter.Strip(canvas, textures.Get("detail-" + role + "-left", canvas), textures.Get("detail-" + role + "-middle", canvas),
            textures.Get("detail-" + role + "-right", canvas), rect);
    }
    public void Panel(Rect2 rect)
    {
        Tile("panel-center", rect);
        Tile("panel-top", new(rect.Position + new Vector2(20, 0), new(rect.Size.X - 40, 20)));
        Tile("panel-bottom", new(rect.Position + new Vector2(20, rect.Size.Y - 20), new(rect.Size.X - 40, 20)));
        Tile("panel-left", new(rect.Position + new Vector2(0, 20), new(20, rect.Size.Y - 40)));
        Tile("panel-right", new(rect.Position + new Vector2(rect.Size.X - 20, 20), new(20, rect.Size.Y - 40)));
        canvas.DrawTexture(textures.Get("detail-panel-top-left", canvas), rect.Position);
        canvas.DrawTexture(textures.Get("detail-panel-top-right", canvas), new(rect.End.X - 20, rect.Position.Y));
        canvas.DrawTexture(textures.Get("detail-panel-bottom-left", canvas), new(rect.Position.X, rect.End.Y - 20));
        canvas.DrawTexture(textures.Get("detail-panel-bottom-right", canvas), rect.End - new Vector2(20, 20));
    }
}
