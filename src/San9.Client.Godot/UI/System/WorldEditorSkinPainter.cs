using Godot;

namespace San9.Client.Godot;

internal sealed class WorldEditorSkinPainter(CanvasItem canvas, TextureScope textures)
{
    private Texture2D Get(string role) => textures.Get("editor-" + role, canvas);
    private void Draw(string role, Vector2 at) => canvas.DrawTexture(Get(role), at);
    public void Frame(Rect2 bounds, float titleWidth)
    {
        var p = bounds.Position; float w = bounds.Size.X, h = bounds.Size.Y;
        var inner = new Rect2(p + new Vector2(39, 44), new(w - 76, h - 97));
        canvas.DrawTextureRectRegion(Get("background"), inner, new(Vector2.Zero, inner.Size));
        // Title and footer have unequal fixed ends; preserve their native transparent margins.
        titleWidth = MathF.Ceiling(titleWidth / 12) * 12;
        SkinPainter.Tile(canvas, Get("frame-title-middle"), new(p + new Vector2(104, 12), new(titleWidth, 80)));
        Draw("frame-title-end", p + new Vector2(104 + titleWidth, 12));
        SkinPainter.Tile(canvas, Get("frame-top-middle"), new(p + new Vector2(248 + titleWidth, 12), new(Math.Max(0, w - 480 - titleWidth), 36)));
        const float footerWidth = 336;
        SkinPainter.Tile(canvas, Get("frame-bottom-middle"), new(p + new Vector2(w - 92 - footerWidth, h - 100), new(footerWidth, 80)));
        Draw("frame-bottom-start", p + new Vector2(w - 236 - footerWidth, h - 100));
        SkinPainter.Tile(canvas, Get("frame-bottom-line"), new(p + new Vector2(196, h - 56), new(Math.Max(0, w - 432 - footerWidth), 36)));
        SkinPainter.Tile(canvas, Get("frame-left"), new(p + new Vector2(20, 132), new(24, h - 308)));
        SkinPainter.Tile(canvas, Get("frame-right"), new(p + new Vector2(w - 44, 132), new(24, h - 308)));
        Draw("frame-top-left", p); Draw("frame-top-right", p + new Vector2(w - 232, 0));
        Draw("frame-bottom-left", p + new Vector2(0, h - 176)); Draw("frame-bottom-right", p + new Vector2(w - 92, h - 172));
    }
    public void Panel(Rect2 rect, Vector2 source)
    {
        canvas.DrawTextureRectRegion(Get("panel"), new(rect.Position + new Vector2(4, 4), rect.Size - new Vector2(8, 8)), new(source, rect.Size - new Vector2(8, 8)));
        foreach (var (part, at, size) in new[] {
            ("top", rect.Position + new Vector2(20,0), new Vector2(rect.Size.X-40,20)),
            ("bottom", new Vector2(rect.Position.X+20,rect.End.Y-20), new Vector2(rect.Size.X-40,20)),
            ("left", rect.Position + new Vector2(0,20), new Vector2(20,rect.Size.Y-40)),
            ("right", new Vector2(rect.End.X-20,rect.Position.Y+20), new Vector2(20,rect.Size.Y-40)) })
            SkinPainter.Tile(canvas, Get("panel-" + part), new(at,size));
        Draw("panel-top-left",rect.Position); Draw("panel-top-right",new(rect.End.X-20,rect.Position.Y));
        Draw("panel-bottom-left",new(rect.Position.X,rect.End.Y-20)); Draw("panel-bottom-right",rect.End-new Vector2(20,20));
    }
    public void Value(Rect2 rect) => SkinPainter.Strip(canvas, Get("field-value-left"), Get("field-value-middle"), Get("field-value-right"), rect);
    public void Caption(Rect2 rect) => SkinPainter.Strip(canvas, Get("caption-left"), Get("caption-middle"), Get("caption-right"), rect);
    public void Label(Rect2 rect) => SkinPainter.Strip(canvas, Get("label-left"), Get("label-middle"), Get("label-right"), rect);
}
