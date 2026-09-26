using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>A logical-viewport modal: outside clicks close it without reaching the map.</summary>
public partial class MapContextPopup : Control
{
    public IReadOnlyDictionary<string, Texture2D> Textures { get; init; } = null!;
    public Action<MapContextEntry> Activated { get; init; } = null!;
    private Rect2 _menu;
    public string Hint => GetChildren().OfType<InformationMenuButton>().FirstOrDefault(b => b.IsHovered()) is {} button
        ? $"查看{button.Title}。" : "";
    public override void _Ready()
    {
        Size = new(1024, 768); ZIndex = 19; MouseFilter = MouseFilterEnum.Stop; Hide();
    }
    public void Open(IReadOnlyList<MapContextEntry> entries, Vector2 point)
    {
        Close();
        if (entries.Count == 0) return;
        Vector2 size = new(104, 32 + entries.Count * 24);
        _menu = new(new(Math.Clamp(point.X - 16, 0, Size.X - size.X), Math.Clamp(point.Y - 16, 0, Size.Y - size.Y)), size);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var button = new InformationMenuButton { Title = entry.Title, Textures = Textures,
                Position = _menu.Position + new Vector2(16, 16 + i * 24), Size = new(72, 24), Disabled = !entry.Enabled };
            button.Pressed += () => { Close(); Activated(entry); };
            AddChild(button);
        }
        Show(); QueueRedraw();
    }
    public void Close()
    {
        Hide();
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
    }
    public override void _Input(InputEvent input)
    {
        if (!Visible) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { Close(); GetViewport().SetInputAsHandled(); }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse && !_menu.HasPoint(mouse.Position)) Close();
        AcceptEvent();
    }
    private void Tile(string part, Rect2 rect)
    {
        var texture = Textures["information-menu-" + part];
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
        {
            Vector2 size = new(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
            DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size));
        }
    }
    public override void _Draw()
    {
        var p = _menu.Position; var s = _menu.Size;
        Tile("center", _menu);
        Tile("top", new(p, new(s.X, 16))); Tile("bottom", new(p + new Vector2(0, s.Y - 16), new(s.X, 16)));
        Tile("left", new(p, new(16, s.Y))); Tile("right", new(p + new Vector2(s.X - 16, 0), new(16, s.Y)));
        DrawTexture(Textures["information-menu-top-left"], p);
        DrawTexture(Textures["information-menu-top-right"], p + new Vector2(s.X - 16, 0));
        DrawTexture(Textures["information-menu-bottom-left"], p + new Vector2(0, s.Y - 16));
        DrawTexture(Textures["information-menu-bottom-right"], p + new Vector2(s.X - 32, s.Y - 32));
    }
}
