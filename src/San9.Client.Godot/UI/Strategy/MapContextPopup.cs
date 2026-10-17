using Godot;
using San9.Rendering;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>A logical-viewport modal: outside clicks close it without reaching the map.</summary>
public partial class MapContextPopup : Control
{
    public Func<IReadOnlyList<OverlayBounds>> Obstacles { get; init; } = () => [];
    private IReadOnlyList<OverlayBounds> _lastObstacles = [];
    public TextureScope Textures { get; init; } = null!;
    public Action<MapContextEntry> Activated { get; init; } = null!;
    private Rect2 _menu;
    private Vector2 _anchor;
    private bool _dismissOnRightRelease;
    public string Hint => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered()) is {} button
        ? $"查看{button.Title}。" : "";
    public override void _Ready()
    {
        Size = GetViewportRect().Size; ZIndex = 19; MouseFilter = MouseFilterEnum.Stop; Hide();
        GetViewport().SizeChanged += Relayout;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= Relayout;
    private void Relayout()
    {
        Size = GetViewportRect().Size;
        _lastObstacles = Obstacles();
        _menu.Position = new(Math.Clamp(_anchor.X - 16, 0, Math.Max(0, Size.X - _menu.Size.X)),
            Math.Clamp(_anchor.Y - 16, 0, Math.Max(0, Size.Y - _menu.Size.Y)));
        var fitted = OverlayLayout.Place(new(_menu.Position.X, _menu.Position.Y, _menu.Size.X, _menu.Size.Y),
            new(0, 0, Size.X, Size.Y), _lastObstacles);
        _menu.Position = new((float)fitted.X, (float)fitted.Y);
        int row = 0;
        foreach (var button in GetChildren().OfType<InformationMenuButton>())
            button.Position = _menu.Position + new Vector2(16, 16 + row++ * 24);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (Visible && !_lastObstacles.SequenceEqual(Obstacles())) Relayout();
    }
    public void Open(IReadOnlyList<MapContextEntry> entries, Vector2 point)
    {
        Close();
        if (entries.Count == 0) return;
        _anchor = point;
        _menu = new(Vector2.Zero, new(104, 32 + entries.Count * 24));
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var button = new InformationMenuButton { Title = entry.Title, Textures = Textures,
                Position = _menu.Position + new Vector2(16, 16 + i * 24), Size = new(72, 24), Disabled = !entry.Enabled };
            button.Pressed += () => { Close(); Activated(entry); };
            AddChild(button);
        }
        Relayout(); Show();
    }
    public void Close()
    {
        Hide();
        _dismissOnRightRelease = false;
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
    }
    public override void _Input(InputEvent input)
    {
        if (!Visible) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            // The release that opened this menu belongs to the map, not to a dismiss gesture.
            if (mouse.Pressed) _dismissOnRightRelease = true;
            else if (_dismissOnRightRelease) Close();
            GetViewport().SetInputAsHandled();
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse && !_menu.HasPoint(mouse.Position)) Close();
        AcceptEvent();
    }
    public override void _Draw() => SkinPainter.InformationMenu(this, Textures, _menu);
}
