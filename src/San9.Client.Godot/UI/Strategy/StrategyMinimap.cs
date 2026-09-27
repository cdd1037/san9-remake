using Godot;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyMinimap : Control
{
    public MapWorld World { get; init; } = null!;
    public San9.Application.GameSession Session { get; init; } = null!;
    public San9.Application.Presentation.StrategySnapshot Scenario => World.View;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private ImageTexture? _terrain;
    private TextureRect? _background;
    private San9.Application.Presentation.StrategySnapshot? _state;
    private readonly Node2D _markers = new();
    private readonly Node2D _viewport = new();
    private readonly Node2D _commandPulse = new();
    private static readonly int[] PulseFrames = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 3, 2, 1];
    private MapObjectTarget? _commandSource;
    private double _pulseTime;
    private int _pulseFrame;
    private (Vector2 Position, float Zoom) _view;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "点击小地图定位";
        var map = World.Package!;
        var pixels = MinimapRenderer.Compile(map.Squares, map.MinimapColors);
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
        _terrain = ImageTexture.CreateFromImage(image);
        var background = _background = new TextureRect { Texture = _terrain, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _markers.Draw += DrawMarkers;
        _viewport.Draw += DrawViewport;
        AddChild(_markers);
        AddChild(_viewport);
        _commandPulse.Draw += DrawCommandPulse;
        AddChild(_commandPulse);
        _view = (World.CameraPosition, World.ZoomFactor);
        Resized += ResizeLayers;
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
    public override void _Process(double delta)
    {
        if (!ReferenceEquals(_state, Scenario)) { _state = Scenario; _markers.QueueRedraw(); _commandPulse.QueueRedraw(); }
        if (_commandSource is not null)
        {
            _pulseTime = (_pulseTime + delta) % 1.7;
            int frame = PulseFrames[(int)(_pulseTime / 0.1)];
            if (frame != _pulseFrame) { _pulseFrame = frame; _commandPulse.QueueRedraw(); }
        }
        var view = (World.CameraPosition, World.ZoomFactor);
        if (_view == view) return;
        _view = view;
        _viewport.QueueRedraw();
    }
    private void ResizeLayers()
    {
        _markers.QueueRedraw();
        _viewport.QueueRedraw();
        _commandPulse.QueueRedraw();
    }
    public void SetCommandSource(MapObjectTarget? source)
    {
        if (_commandSource == source) return;
        _commandSource = source;
        _pulseTime = 0; _pulseFrame = 0;
        _commandPulse.QueueRedraw();
    }
    private void DrawCommandPulse()
    {
        if (_commandSource is not { } source || _pulseFrame == 0) return;
        double x, y;
        if (source.IsUnit)
        {
            var unit = Scenario.World.Units.FirstOrDefault(u => u.Id == source.Id);
            if (unit is null) return;
            x = (unit.Position.X - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell;
            y = (unit.Position.Y - MapCoordinates.UnitsPerCell / 2) / (double)MapCoordinates.UnitsPerCell;
        }
        else
        {
            var settlement = Scenario.Settlements.FirstOrDefault(s => s.Id == source.Id);
            if (settlement is null) return;
            x = settlement.X; y = settlement.Y;
        }
        var point = MinimapGeometry.FromGrid(x, y);
        var scale = Size / new Vector2(320, 160);
        var center = new Vector2((float)point.X * Size.X, (float)point.Y * Size.Y) - new Vector2(0, scale.Y);
        var texture = Textures[$"minimap-command-pulse-{_pulseFrame}"];
        var size = texture.GetSize() * scale;
        _commandPulse.DrawTextureRect(texture, new(center - size / 2, size), false);
    }
    private void DrawMarkers()
    {
        foreach (var s in Scenario.Settlements.Where(s => s.CityMarkerColor is not null))
        {
            var p = MinimapGeometry.FromGrid(s.X, s.Y); var position = new Vector2((float)p.X * Size.X, (float)p.Y * Size.Y);
            var color = new Color(Skin.Colors[$"city-marker-{s.CityMarkerColor}"]);
            position = position.Floor();
            _markers.DrawRect(new(position - Vector2.One * 4, new Vector2(8, 8)), new Color("080808"));
            _markers.DrawRect(new(position - Vector2.One * 3, new Vector2(6, 6)), color);
        }
    }
    private void DrawViewport()
    {
        var half = new Vector2(ClientLayout.LogicalWidth, ClientLayout.LogicalHeight) / (2 * _view.Zoom);
        var a = MinimapGeometry.FromWorld(_view.Position.X - half.X, _view.Position.Y - half.Y);
        var b = MinimapGeometry.FromWorld(_view.Position.X + half.X, _view.Position.Y + half.Y);
        var polygon = MinimapGeometry.ClipViewport(a.U, a.V, b.U, b.V).Select(p => new Vector2((float)p.U * Size.X, (float)p.V * Size.Y)).ToArray();
        if (polygon.Length >= 3)
        {
            _viewport.DrawPolyline([.. polygon, polygon[0]], Colors.White, 1);
        }
    }
    public override void _ExitTree()
    {
        if (_background is not null) _background.Texture = null;
        _terrain?.Dispose(); _terrain = null;
    }
}
