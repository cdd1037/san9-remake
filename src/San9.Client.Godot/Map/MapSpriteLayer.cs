using Godot;

namespace San9.Client.Godot;

internal partial class MapSpriteLayer : Node2D
{
    private readonly List<Sprite2D> _sprites = [];
    private int _cursor;
    public void Begin() => _cursor = 0;
    public void Add(Texture2D texture, Vector2 position, Material? material)
    {
        if (_cursor == _sprites.Count)
        { var sprite = new Sprite2D { Centered = false }; AddChild(sprite); _sprites.Add(sprite); }
        var item = _sprites[_cursor++];
        item.Texture = texture; item.Position = position; item.Visible = true;
        item.Material = material; item.Modulate = Colors.White;
    }
    public void End()
    {
        for (int i = _cursor; i < _sprites.Count; i++)
        { _sprites[i].Texture = null; _sprites[i].Visible = false; }
    }
    public void Clear() { _cursor = 0; End(); }
}
