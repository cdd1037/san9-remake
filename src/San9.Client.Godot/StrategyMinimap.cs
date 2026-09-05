using Godot;
using San9.Content.Scenarios;
using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyMinimap : Control
{
    public MapWorld World { get; init; } = null!;
    public ScenarioSnapshot Scenario { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    private ImageTexture? _terrain;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "点击小地图定位";
        var pixels = World.AssetPixels("minimap-terrain");
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
        _terrain = ImageTexture.CreateFromImage(image);
    }
    public override bool _HasPoint(Vector2 point) => MinimapGeometry.TryGrid(point.X / Size.X, point.Y / Size.Y, out _, out _);
    public bool Navigate(Vector2 point)
    {
        if (!MinimapGeometry.TryGrid(point.X / Size.X, point.Y / Size.Y, out double x, out double y)) return false;
        var world = new Vector2((float)(32 * (x + 200 - y) + 96), (float)(16 * (x + y + 1) + 112));
        World.FocusWorld(world); return true;
    }
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) Navigate(b.Position);
        AcceptEvent();
    }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        if (_terrain is null) return;
        DrawTextureRect(_terrain, new(Vector2.Zero, Size), false);
        foreach (var s in Scenario.Settlements.Where(s => s.CityMarkerColor is not null))
        {
            var p = MinimapGeometry.FromGrid(s.X, s.Y); var position = new Vector2((float)p.X * Size.X, (float)p.Y * Size.Y);
            var color = new Color(Skin.Colors[$"city-marker-{s.CityMarkerColor}"]);
            position = position.Floor();
            DrawRect(new(position - Vector2.One * 4, new Vector2(8, 8)), new Color("080808"));
            DrawRect(new(position - Vector2.One * 3, new Vector2(6, 6)), color);
        }
        var half = new Vector2(512, 384) / World.ZoomFactor;
        var a = MinimapGeometry.FromWorld(World.CameraPosition.X - half.X, World.CameraPosition.Y - half.Y);
        var b = MinimapGeometry.FromWorld(World.CameraPosition.X + half.X, World.CameraPosition.Y + half.Y);
        var polygon = MinimapGeometry.ClipViewport(a.U, a.V, b.U, b.V).Select(p => new Vector2((float)p.U * Size.X, (float)p.V * Size.Y)).ToArray();
        if (polygon.Length >= 3)
        {
            DrawPolyline([.. polygon, polygon[0]], Colors.White, 1);
        }
    }
    public override void _ExitTree() => _terrain?.Dispose();
}
