using Godot;

namespace San9.Client.Godot;

public partial class MapWorld : Node2D
{
    private Camera2D _camera = null!;

    public float ZoomFactor => _camera.Zoom.X;

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera");
        _camera.Position = new Vector2(
            ClientLayout.LogicalWidth / 2f,
            ClientLayout.LogicalHeight / 2f);

        ColorRect background = GetNode<ColorRect>("Background");
        background.Size = new Vector2(
            ClientLayout.LogicalWidth,
            ClientLayout.LogicalHeight);
    }

    public void SetZoom(float zoomFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoomFactor);
        _camera.Zoom = Vector2.One * zoomFactor;
    }

    public Vector2 ViewportToWorld(Vector2 viewportPosition) =>
        GetViewport().CanvasTransform.AffineInverse() * viewportPosition;
}
