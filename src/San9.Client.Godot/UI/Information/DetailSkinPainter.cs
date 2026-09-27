using Godot;

namespace San9.Client.Godot;

internal sealed class DetailSkinPainter(CanvasItem canvas, TextureScope textures)
{
    public void Tile(string role, Rect2 rect)
    {
        SkinPainter.Tile(canvas, textures["detail-" + role], rect);
    }
    public void Strip(string role, Rect2 rect)
    {
        SkinPainter.Strip(canvas, textures["detail-" + role + "-left"], textures["detail-" + role + "-middle"],
            textures["detail-" + role + "-right"], rect);
    }
    public void Panel(Rect2 rect)
    {
        Tile("panel-center", rect);
        Tile("panel-top", new(rect.Position + new Vector2(20, 0), new(rect.Size.X - 40, 20)));
        Tile("panel-bottom", new(rect.Position + new Vector2(20, rect.Size.Y - 20), new(rect.Size.X - 40, 20)));
        Tile("panel-left", new(rect.Position + new Vector2(0, 20), new(20, rect.Size.Y - 40)));
        Tile("panel-right", new(rect.Position + new Vector2(rect.Size.X - 20, 20), new(20, rect.Size.Y - 40)));
        canvas.DrawTexture(textures["detail-panel-top-left"], rect.Position);
        canvas.DrawTexture(textures["detail-panel-top-right"], new(rect.End.X - 20, rect.Position.Y));
        canvas.DrawTexture(textures["detail-panel-bottom-left"], new(rect.Position.X, rect.End.Y - 20));
        canvas.DrawTexture(textures["detail-panel-bottom-right"], rect.End - new Vector2(20, 20));
    }
}
