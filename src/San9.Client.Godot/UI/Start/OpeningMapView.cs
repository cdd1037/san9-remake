using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Shared opening geography. Both rendering and picking use the authored city areas.</summary>
public partial class OpeningMapView : Control
{
    public OpeningMapDefinition Definition { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureScope? SelectionTextures { get; init; }
    public IReadOnlyDictionary<int, string> CityColors { get; set; } = null!;
    public IReadOnlyDictionary<int, int> Targets { get; init; } = new Dictionary<int, int>();
    public OpeningMapPreview? Preview { get; init; }
    public string? MarkerAsset { get; init; }
    public Action<int?>? Hovered { get; init; }
    public Action<int>? Activated { get; init; }
    public IReadOnlySet<int> Selected { get; set; } = new HashSet<int>();
    public bool FocusOnReady { get; init; } = true;
    private int? _hovered;
    private int? _pressed;
    public override void _Ready()
    {
        Size = new(Definition.Width, Definition.Height);
        MouseFilter = Activated is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        if (Activated is not null) { FocusMode = FocusModeEnum.All; if (FocusOnReady) GrabFocus(); }
        MouseExited += () => Hover(null);
    }
    private int? Target(Vector2 point)
    {
        var city = Definition.Cities.FirstOrDefault(c => CityColors.ContainsKey(c.Key) &&
            new Rect2(c.Value.Center.X - 12, c.Value.Center.Y - 12, 24, 24).HasPoint(point));
        int? id = city.Value is null ? Definition.CityAt((int)Math.Floor(point.X), (int)Math.Floor(point.Y)) : city.Key;
        return id is int found && Targets.TryGetValue(found, out int target) ? target : null;
    }
    private void Hover(int? id)
    {
        if (_hovered == id) return;
        _hovered = id; Hovered?.Invoke(id); QueueRedraw();
    }
    public override void _GuiInput(InputEvent input)
    {
        // A single-choice callback can remove this map from the scene immediately.
        AcceptEvent();
        if (input is InputEventMouseMotion motion) Hover(Target(motion.Position));
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            int? target = Target(click.Position);
            if (click.Pressed) _pressed = target;
            else { if (target is int id && _pressed == target) Activated?.Invoke(id); _pressed = null; }
            QueueRedraw();
        }
    }
    public override void _Draw()
    {
        if (!Textures.AreReady(Definition.Assets)) { Textures.BindRedraw(this); return; }
        if (Preview is { } preview)
        {
            DrawTexture(Textures.Get(preview.Background.Asset, this), Vector2.Zero);
            if (CityColors.ContainsKey(Definition.Inset.CityId))
                DrawTexture(Textures.Get(preview.Inset.Asset, this), new(preview.Inset.X, preview.Inset.Y));
            foreach (var (id, color) in CityColors)
            {
                if (Definition.Colors[color].PreviewMarker is not { } marker) continue;
                var center = preview.Project(Definition.Cities[id].Center);
                DrawTexture(Textures.Get(marker, this), new((float)Math.Round(center.X) - 8, (float)Math.Round(center.Y) - 8));
            }
            return;
        }
        DrawTexture(Textures.Get(Definition.Terrain, this), Vector2.Zero);
        var inset = Definition.Inset;
        if (CityColors.ContainsKey(inset.CityId)) DrawTexture(Textures.Get(inset.Sprite.Asset, this), new(inset.Sprite.X, inset.Sprite.Y));
        foreach (var (id, city) in Definition.Cities)
        {
            if (!CityColors.TryGetValue(id, out var colorId)) continue;
            Color color = new(Definition.Colors[colorId].Rgb);
            color.A = 72f / 128;
            foreach (var run in city.Area) DrawRect(new(run.X, run.Y, run.Length, 1), color);
        }
        DrawTexture(Textures.Get(Definition.Borders, this), Vector2.Zero);
        foreach (var (id, city) in Definition.Cities)
        {
            if (!CityColors.TryGetValue(id, out var color)) continue;
            int? target = Targets.TryGetValue(id, out int selectable) ? selectable : null;
            var center = new Vector2(city.Center.X, city.Center.Y);
            bool selected = target is int entity && Selected.Contains(entity);
            if (selected && SelectionTextures is { } skin)
            {
                var selection = skin.Get("information-map-selected", this);
                DrawTexture(selection, center - selection.GetSize() / 2);
            }
            var texture = Textures.Get(MarkerAsset ?? Definition.Colors[color].Marker, this);
            float width = texture.GetWidth() / 4f;
            int frame = target is null ? 3 : _hovered == target ? _pressed == target ? 2 : 1 : 0;
            DrawTextureRectRegion(texture, new(center - new Vector2(width / 2, texture.GetHeight() / 2f), new(width, texture.GetHeight())),
                new(frame * width, 0, width, texture.GetHeight()));
            if (selected && SelectionTextures is null)
            {
                DrawArc(center, 13, 0, Mathf.Tau, 32, Colors.Black, 4, true);
                DrawArc(center, 13, 0, Mathf.Tau, 32, Colors.White, 2, true);
            }
        }
    }
}
