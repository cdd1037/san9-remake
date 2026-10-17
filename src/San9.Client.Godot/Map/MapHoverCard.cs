using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class MapHoverCard : Node2D
{
    public TextureScope Textures { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public Font UiFont { get; init; } = null!;
    public MapHoverInfo? Info { get; private set; }
    public Func<IReadOnlyList<San9.Rendering.OverlayBounds>> Obstacles { get; init; } = () => [];
    private Vector2 _anchor;
    public override void _Ready() => GetViewport().SizeChanged += Relayout;
    public override void _Process(double delta) { if (Visible) Relayout(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= Relayout;

    private void Relayout()
    {
        if (Info is null) return;
        var size = GetViewportRect().Size;
        var p = San9.Rendering.BuildingHoverGeometry.Place(_anchor.X, _anchor.Y, Info.Height, size.X, size.Y);
        var placed = San9.Rendering.OverlayLayout.Place(new(p.X, p.Y, San9.Rendering.BuildingHoverGeometry.Width, Info.Height),
            new(0, 0, size.X, size.Y), Obstacles());
        Position = new((float)placed.X, (float)placed.Y);
    }

    public void ShowInfo(MapHoverInfo? info, Vector2 point)
    {
        if (info?.Id != Info?.Id && info is not null) _anchor = point;
        if (info is not null) World.PrepareHoverFlag(info.Force?.ColorCode ?? 50,
            info.IsRulerCommander ? World.Package!.Units.RulerFlagGlyph : info.FlagGlyph ?? info.Force?.FlagGlyph ?? -1);
        Info = info; Visible = info is not null; Relayout(); QueueRedraw();
    }

    public override void _Draw()
    {
        if (Info is not { } info) return;
        DrawTexture(Textures[$"hover-{info.Theme.ToString().ToLowerInvariant()}-{(info.Height == 316 ? "city" : info.Height == 240 ? "unit" : "facility")}"], Vector2.Zero);
        World.DrawHoverFlag(this, info.Force?.ColorCode ?? 50, info.IsRulerCommander ? World.Package!.Units.RulerFlagGlyph : info.FlagGlyph ?? info.Force?.FlagGlyph ?? -1, new(-13, 12));
        Text(info.Name, new(64, 42), new Color("f0f0f0"), false, info.Height == 316 ? 32 : info.Height == 240 ? 80 : 48);
        foreach (string icon in info.Icons) DrawTexture(Textures["icon-" + icon], new(icon == "emperor" ? 124 : 102, 26));
        for (int i = 0; i < info.Rows.Count; i++)
        {
            var row = info.Rows[i]; float y = 64 + 24 * i;
            Text(row.Label, new(16, y + 18), new Color("f8f8b0"), false, 32);
            Text(row.Value, new(64, y + 18), new Color("f0f0f0"), row.Numeric, 80);
            Tile("hover-divider", new(16, y + 22, 128, Textures["hover-divider"].GetHeight()));
        }
    }
    private void Text(string value, Vector2 point, Color color, bool numeric, int width) =>
        GameTextPresenter.Draw(this, UiFont, value, point, GameTextStyles.CatalogRow with { Foreground = color },
            numeric ? HorizontalAlignment.Right : HorizontalAlignment.Center, width);
    private void Tile(string role, Rect2 rect)
    {
        SkinPainter.Tile(this, Textures[role], rect);
    }
}
