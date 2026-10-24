using Godot;

namespace San9.Client.Godot;

internal partial class MapSpriteLayer : Node2D
{
    private sealed class Entry(Sprite2D sprite)
    {
        public Sprite2D Sprite = sprite;
        public Texture2D? Texture;
        public Vector2 Position;
        public Material? Material;
        public Color Modulate = Colors.White;
        public bool Visible;
    }
    private readonly List<Entry> _sprites = [];
    private int _cursor, _visible;
    public void Begin() => _cursor = 0;
    public void Add(Texture2D texture, Vector2 position, Material? material, Color? modulate = null)
    {
        if (_cursor == _sprites.Count)
        { var sprite = new Sprite2D { Centered = false, Visible = false }; AddChild(sprite); _sprites.Add(new(sprite)); }
        var item = _sprites[_cursor++];
        if (item.Texture != texture) { item.Sprite.Texture = texture; item.Texture = texture; }
        if (item.Position != position) { item.Sprite.Position = position; item.Position = position; }
        if (!item.Visible) { item.Sprite.Visible = true; item.Visible = true; }
        if (item.Material != material) { item.Sprite.Material = material; item.Material = material; }
        var color = modulate ?? Colors.White;
        if (item.Modulate != color) { item.Sprite.Modulate = color; item.Modulate = color; }
    }
    public void End()
    {
        for (int i = _cursor; i < _visible; i++)
        {
            var item = _sprites[i]; item.Sprite.Texture = null; item.Texture = null;
            item.Sprite.Visible = false; item.Visible = false;
        }
        _visible = _cursor;
    }
    public void Clear() { _cursor = 0; End(); }
}
