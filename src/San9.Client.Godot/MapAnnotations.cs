using Godot;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapAnnotations : Node2D
{
    public MapWorld World { get; init; } = null!;
    public StrategySelection Selection { get; init; } = null!;
    public ScenarioSnapshot Scenario { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public IReadOnlyDictionary<string, PngImage> SkinImages { get; init; } = null!;
    public Font UiFont { get; init; } = null!;
    private readonly Dictionary<string, ImageTexture> _textures = [];
    private Dictionary<int, SettlementSnapshot> _settlements = [];
    public override void _Ready()
    {
        _settlements = Scenario.Settlements.ToDictionary(s => s.Id);
        foreach (var pair in Skin.Roles.Where(p => p.Key.StartsWith("bar-") || p.Key.StartsWith("icon-")))
        {
            var p = SkinImages[pair.Value];
            using var image = Image.CreateFromData(p.Width, p.Height, false, Image.Format.Rgba8, p.Pixels);
            _textures.Add(pair.Key, ImageTexture.CreateFromImage(image));
        }
    }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        if (World.Package is not { } map || World.IsLoading) return;
        foreach (var b in map.Buildings)
        {
            var anchor = BuildingInfoGeometry.Anchor(b.X, b.Y, b.FixedType, map.Squares[b.Y * 200 + b.X].Elevation);
            var point = World.WorldToViewport(new(anchor.X, anchor.Y));
            float zoom = World.ZoomFactor;
            if (!new Rect2(-116 * zoom, -55 * zoom, 1024 + 116 * zoom, 768 + 55 * zoom).HasPoint(point)) continue;
            DrawSetTransform(point, 0, Vector2.One * zoom);
            var s = _settlements[b.Id];
            string soldiers = s.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float width = UiFont.GetStringSize(soldiers, fontSize: 16).X;
            Text(soldiers, new(72 - width, 16));
            Bar("durability", s.Durability, s.MaxDurability, new(0, 16));
            Bar("morale", s.Morale, 100, new(0, 29));
            Text(Texts.Resolve(s.NameKey), new(0, 53));
            foreach (string icon in s.Icons) DrawTexture(_textures["icon-" + icon], icon == "emperor" ? new(96, 17) : new(75, 17));
        }
        DrawSetTransform(Vector2.Zero);
    }
    private void Bar(string kind, int current, int max, Vector2 position)
    {
        DrawTexture(_textures[$"bar-{kind}-frame"], position);
        int width = BuildingInfoGeometry.FillWidth(current, max);
        if (width == 0) return;
        var fill = _textures[$"bar-{kind}-{BuildingInfoGeometry.FillState(current, max)}"];
        DrawTextureRectRegion(fill, new(position + new Vector2(4, 4), new Vector2(width, fill.GetHeight())), new(0, 0, width, fill.GetHeight()));
    }
    private void Text(string text, Vector2 point) =>
        GameTextPresenter.Draw(this, UiFont, text, point, GameTextStyles.For(GameTextRole.Building));
    public override void _ExitTree() { foreach (var texture in _textures.Values) texture.Dispose(); }
}
