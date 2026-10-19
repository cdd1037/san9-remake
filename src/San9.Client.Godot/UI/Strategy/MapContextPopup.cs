using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public sealed record MapPopupEntry(string Title, string Hint, bool Enabled, Action? Activate = null,
    IReadOnlyList<MapPopupEntry>? Children = null, bool SeparatorBefore = false,
    string SkinPrefix = "information-menu-button");

/// <summary>A logical-viewport modal: outside clicks close it without reaching the map.</summary>
public partial class MapContextPopup : Control
{
    public TextureScope Textures { get; init; } = null!;
    private readonly List<(InformationMenuButton Button, MapPopupEntry Entry)> _primary = [], _secondary = [];
    private InformationMenuButton? _expanded;
    private Rect2? _submenu;
    private Rect2 _menu;
    private Vector2 _anchor;
    private bool _dismissOnRightRelease;
    public IReadOnlyList<InformationMenuButton> Buttons => _primary.Select(item => item.Button).ToArray();
    public InformationMenuButton? HoverButton => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered());
    public string Hint => HoverButton?.HintText ?? "";
    public bool HintEnabled => HoverButton?.Disabled != true;
    public override void _Ready()
    {
        Size = GetViewportRect().Size; ZIndex = 19; MouseFilter = MouseFilterEnum.Stop; Hide();
        GetViewport().SizeChanged += Relayout;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= Relayout;
    private void Relayout()
    {
        Size = GetViewportRect().Size;
        _menu.Position = new(Math.Clamp(_anchor.X - 18, 0, Math.Max(0, Size.X - _menu.Size.X)),
            Math.Clamp(_anchor.Y - 18, 0, Math.Max(0, Size.Y - 32 - _menu.Size.Y)));
        float y = 16;
        foreach (var (button, entry) in _primary)
        {
            if (entry.SeparatorBefore) y += 16;
            button.Position = _menu.Position + new Vector2(16, y);
            y += 24;
        }
        if (_expanded is not null) LayoutSubmenu();
        QueueRedraw();
    }
    public void Open(IReadOnlyList<MapPopupEntry> entries, Vector2 point, int buttonWidth = 72)
    {
        Close();
        if (entries.Count == 0) return;
        _anchor = point;
        _menu = new(Vector2.Zero, new(32 + buttonWidth, 32 + entries.Count * 24 + entries.Count(e => e.SeparatorBefore) * 16));
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _primary.Add((AddButton(entry, buttonWidth), entry));
        }
        Relayout(); Show();
    }
    private InformationMenuButton AddButton(MapPopupEntry entry, int width, bool secondary = false)
    {
        var button = new InformationMenuButton { Title = entry.Title, HintText = entry.Hint, Textures = Textures,
            Size = new(width, 24), Disabled = !entry.Enabled,
            SkinPrefix = secondary ? "shortcut-submenu" : entry.SkinPrefix };
        button.Pressed += () =>
        {
            if (entry.Children is { Count: > 0 }) Expand(entry.Children, button);
            else { Close(); entry.Activate?.Invoke(); }
        };
        AddChild(button);
        return button;
    }
    private void Expand(IReadOnlyList<MapPopupEntry> entries, InformationMenuButton button)
    {
        foreach (var item in _secondary) { RemoveChild(item.Button); item.Button.QueueFree(); }
        _secondary.Clear(); _expanded = button;
        foreach (var entry in entries) _secondary.Add((AddButton(entry, 72, true), entry));
        LayoutSubmenu(); QueueRedraw();
    }
    private void LayoutSubmenu()
    {
        var (x, y) = ContextMenuGeometry.Submenu(_expanded!.Position.X, _expanded.Position.Y, _expanded.Size.X,
            72, _secondary.Count * 24, Size.X, Size.Y - 32);
        _submenu = new(new(x, y), new(72, _secondary.Count * 24));
        for (int i = 0; i < _secondary.Count; i++) _secondary[i].Button.Position = new(x, y + i * 24);
    }
    public void Close()
    {
        Hide();
        _dismissOnRightRelease = false;
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _primary.Clear(); _secondary.Clear(); _expanded = null; _submenu = null;
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
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse &&
            !_menu.HasPoint(mouse.Position) && _submenu?.HasPoint(mouse.Position) != true) Close();
        AcceptEvent();
    }
    public override void _Draw()
    {
        SkinPainter.InformationMenu(this, Textures, _menu);
        foreach (var (button, entry) in _primary.Where(item => item.Entry.SeparatorBefore))
            SkinPainter.Tile(this, Textures.Get("shortcut-separator", this), new(button.Position + new Vector2(-4, -12), new(button.Size.X + 8, 8)));
    }
}
