using Godot;

namespace San9.Client.Godot;

internal sealed class JournalSkinPainter(CanvasItem canvas, TextureScope textures)
{
    public void Draw(float panelWidth, float titleWidth)
    {
        canvas.DrawTexture(textures.Get("detail-background", canvas), Vector2.Zero);
        canvas.DrawTexture(textures.Get("progress-record-side", canvas), new(960, 154));
        new DetailSkinPainter(canvas, textures).Panel(new(76, 100, panelWidth, 586));
        SkinPainter.Strip(canvas, textures.Get("detail-title-left", canvas), textures.Get("detail-title-middle", canvas), textures.Get("detail-title-right", canvas), new(56, 35, titleWidth, 60));
        string[] parts = ["top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"];
        float middleWidth = panelWidth - 50;
        float[] xs = [92, 101, 101 + middleWidth], ys = [116, 141, 645], widths = [9, middleWidth, 12], heights = [25, 504, 28];
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            SkinPainter.Tile(canvas, textures.Get("progress-record-" + parts[y * 3 + x], canvas), new(xs[x], ys[y], widths[x], heights[y]));
    }
}
