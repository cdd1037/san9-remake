using Godot;

namespace San9.Client.Godot;

internal sealed class SaveLoadSkinPainter(CanvasItem canvas, TextureScope textures)
{
    public void Frame(Rect2 bounds, bool note = false)
    {
        string rightRole = note ? "frame-note-top-right" : "frame-top-right";
        Tile("frame-top", new(bounds.Position, new(bounds.Size.X, 8)));
        Tile("frame-bottom", new(bounds.Position + new Vector2(0, bounds.Size.Y - 8), new(bounds.Size.X, 8)));
        Tile("frame-left", new(bounds.Position, new(8, bounds.Size.Y)));
        Tile("frame-right", new(bounds.Position + new Vector2(bounds.Size.X - 8, 0), new(8, bounds.Size.Y)));
        canvas.DrawTexture(textures.Get("save-load-frame-top-left", canvas), bounds.Position);
        canvas.DrawTexture(textures.Get("save-load-" + rightRole, canvas), new(bounds.End.X - 128, bounds.Position.Y));
        canvas.DrawTexture(textures.Get("save-load-frame-bottom-left", canvas), new(bounds.Position.X, bounds.End.Y - 64));
        canvas.DrawTexture(textures.Get("save-load-frame-bottom-right", canvas), bounds.End - new Vector2(64, 64));
    }
    public void Title(Vector2 at, float width) => SkinPainter.Strip(canvas, textures.Get("save-load-title-left", canvas),
        textures.Get("save-load-title-middle", canvas), textures.Get("save-load-title-right", canvas), new(at, new(width, 48)));
    public void Background(Rect2 bounds) => Tile("background", bounds);
    private void Tile(string role, Rect2 bounds) => SkinPainter.Tile(canvas, textures.Get("save-load-" + role, canvas), bounds);
}
