using Godot;
using San9.TerrainEditor.Model;

namespace San9.TerrainEditor;

public partial class TerrainView : Control
{
    private ImageTexture? texture;
    private PreviewFrame? frame;
    private Vector2 origin;
    private float zoom = 0.55f;
    private bool panning, painting;
    private int hover = -1;
    public Action<int, bool>? PaintCell;
    public Action? StrokeEnded;
    public Action<int>? SampleCell;
    public Action<int>? HoverCell;
    public int BrushRadius { get; set; }
    public int DocumentWidth { get; set; } = 32;
    public PreviewFrame? Frame => frame;

    public override void _Ready()
    {
        ClipContents = true; MouseDefaultCursorShape = CursorShape.Cross;
        TextureFilter = TextureFilterEnum.Nearest;
        Resized += QueueRedraw;
        MouseExited += () => { hover = -1; QueueRedraw(); };
    }

    public void Present(PreviewFrame next)
    {
        using var image = Image.CreateFromData(next.Width, next.Height, false, Image.Format.Rgba8, next.Pixels);
        if (texture is null || texture.GetWidth() != next.Width || texture.GetHeight() != next.Height)
        {
            texture?.Dispose(); texture = ImageTexture.CreateFromImage(image);
        }
        else texture.Update(image);
        bool first = frame is null; frame = next;
        if (first) Fit(); QueueRedraw();
    }

    public void Fit()
    {
        if (frame is null) return;
        zoom = Math.Clamp(Math.Min(Size.X / frame.Width, Size.Y / frame.Height) * .95f, .1f, 4f);
        origin = (Size - new Vector2(frame.Width, frame.Height) * zoom) / 2; QueueRedraw();
    }

    public int Pick(Vector2 local)
    {
        if (frame is null) return -1;
        Vector2 p = (local - origin) / zoom;
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        return x < 0 || y < 0 || x >= frame.Width || y >= frame.Height ? -1 : frame.Owners[y * frame.Width + x];
    }

    public Vector2? VisiblePoint(int cell)
    {
        if (frame is null) return null;
        for (int i = frame.Owners.Length / 2; i < frame.Owners.Length; i++)
            if (frame.Owners[i] == cell) return origin + new Vector2(i % frame.Width + .5f, i / frame.Width + .5f) * zoom;
        for (int i = 0; i < frame.Owners.Length / 2; i++)
            if (frame.Owners[i] == cell) return origin + new Vector2(i % frame.Width + .5f, i / frame.Width + .5f) * zoom;
        return null;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("17221f"));
        if (texture is not null) DrawTextureRect(texture, new Rect2(origin, texture.GetSize() * zoom), false);
        if (hover >= 0)
        {
            // 圆环是工具大小提示；精确拾取来自当前可见像素的归属图。
            DrawArc(GetLocalMousePosition(), Math.Max(6, (BrushRadius + .5f) * 32 * zoom), 0, Mathf.Tau, 48, new Color("f4d798"), 1.5f, true);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton b)
        {
            if (b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && b.Pressed)
            {
                float old = zoom; zoom = Math.Clamp(zoom * (b.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), .1f, 4f);
                origin = b.Position - (b.Position - origin) * (zoom / old); QueueRedraw(); AcceptEvent(); return;
            }
            if (b.ButtonIndex is MouseButton.Right or MouseButton.Middle) { panning = b.Pressed; AcceptEvent(); return; }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (b.Pressed)
                {
                    int cell = Pick(b.Position);
                    if (cell >= 0)
                    {
                        if (b.CtrlPressed) SampleCell?.Invoke(cell);
                        else { painting = true; PaintCell?.Invoke(cell, true); }
                    }
                }
                else FinishStroke();
                AcceptEvent();
            }
        }
        if (@event is InputEventMouseMotion motion)
        {
            if (panning) origin += motion.Relative;
            hover = Pick(motion.Position); HoverCell?.Invoke(hover);
            if (painting && hover >= 0) PaintCell?.Invoke(hover, false);
            QueueRedraw();
        }
    }
    public override void _Input(InputEvent @event)
    {
        // 松开鼠标即提交笔画，即使指针已移到侧栏或窗口外。
        if (@event is InputEventMouseButton { Pressed: false } b)
        {
            if (b.ButtonIndex == MouseButton.Left) FinishStroke();
            if (b.ButtonIndex is MouseButton.Right or MouseButton.Middle) panning = false;
        }
    }
    public void FinishStroke()
    {
        if (!painting) return; painting = false; StrokeEnded?.Invoke();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) { FinishStroke(); panning = false; }
    }
    public override void _ExitTree() => texture?.Dispose();
}
