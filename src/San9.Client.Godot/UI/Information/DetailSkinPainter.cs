using Godot;

namespace San9.Client.Godot;

internal sealed class DetailSkinPainter(CanvasItem canvas, IReadOnlyDictionary<string, Texture2D> textures)
{
    public void Tile(string role, Rect2 rect)
    {
        var texture = textures["detail-" + role];
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
        {
            var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
            canvas.DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size));
        }
    }
    public void Strip(string role, Rect2 rect)
    {
        var left = textures["detail-" + role + "-left"]; var right = textures["detail-" + role + "-right"];
        Tile(role + "-middle", new(rect.Position + new Vector2(left.GetWidth(), 0), new(rect.Size.X - left.GetWidth() - right.GetWidth(), rect.Size.Y)));
        canvas.DrawTextureRect(left, new(rect.Position, new(left.GetWidth(), rect.Size.Y)), false);
        canvas.DrawTextureRect(right, new(new(rect.End.X - right.GetWidth(), rect.Position.Y), new(right.GetWidth(), rect.Size.Y)), false);
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
