using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Package cursor images and hotspots, held through the shared texture service.</summary>
internal sealed class EdgeScrollCursor : IDisposable
{
    private TextureScope? _textures;
    private IReadOnlyDictionary<string, CursorDefinition> _definitions = null!;
    private (EdgeScrollDirection Direction, bool Active) _current;

    public void Initialize(TextureService service)
    {
        if (_textures is not null) return;
        _definitions = service.Cursors;
        _textures = service.CreateScope(service.CursorAssets);
        _textures.Request(CursorDefinition.EdgeScrollRoles);
    }

    public void Show(EdgeScrollDirection direction, bool active)
    {
        if (direction.IsNone) active = false;
        var key = (direction, active);
        if (_current == key) return;
        if (direction.IsNone)
        {
            Input.SetCustomMouseCursor(null);
            _current = key;
            return;
        }
        string name = (direction.X, direction.Y) switch
        {
            (0, -1) => "up", (1, 0) => "right", (0, 1) => "down", (-1, 0) => "left",
            (1, -1) => "up-right", (1, 1) => "down-right", (-1, 1) => "down-left", (-1, -1) => "up-left",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        string role = $"scroll-{name}-{(active ? "active" : "idle")}";
        if (_textures is null || !_textures.TryGetReadyResource(role, out var texture))
        {
            Show(default, false);
            return;
        }
        var definition = _definitions[role];
        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, new Vector2(definition.HotspotX, definition.HotspotY));
        _current = key;
    }

    public void Dispose()
    {
        Show(default, false);
        _textures?.Dispose();
        _textures = null;
    }
}
