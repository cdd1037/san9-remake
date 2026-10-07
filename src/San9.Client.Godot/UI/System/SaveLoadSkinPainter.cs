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
        canvas.DrawTexture(textures["save-load-frame-top-left"], bounds.Position);
        canvas.DrawTexture(textures["save-load-" + rightRole], new(bounds.End.X - 128, bounds.Position.Y));
        canvas.DrawTexture(textures["save-load-frame-bottom-left"], new(bounds.Position.X, bounds.End.Y - 64));
        canvas.DrawTexture(textures["save-load-frame-bottom-right"], bounds.End - new Vector2(64, 64));
    }
    public void Title(Vector2 at, float width) => SkinPainter.Strip(canvas, textures["save-load-title-left"],
        textures["save-load-title-middle"], textures["save-load-title-right"], new(at, new(width, 48)));
    public void Background(Rect2 bounds) => Tile("background", bounds);
    private void Tile(string role, Rect2 bounds) => SkinPainter.Tile(canvas, textures["save-load-" + role], bounds);
}
