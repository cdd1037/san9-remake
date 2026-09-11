using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

public partial class BuildingHoverCard : Node2D
{
    public IReadOnlyDictionary<string, ImageTexture> Textures { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public Font UiFont { get; init; } = null!;
    public BuildingHoverInfo? Info { get; private set; }
    public void ShowInfo(BuildingHoverInfo? info, Vector2 point)
    {
        if (info?.Id != Info?.Id && info is not null)
        {
            var p = San9.Rendering.BuildingHoverGeometry.Place(point.X, point.Y, info.Height);
            Position = new(p.X, p.Y);
        }
        Info = info; Visible = info is not null; QueueRedraw();
    }

    public override void _Draw()
    {
        if (Info is not { } info) return;
        DrawTexture(Textures[$"hover-{info.Theme.ToString().ToLowerInvariant()}-{(info.Height == 316 ? "city" : "facility")}"], Vector2.Zero);
        World.DrawHoverFlag(this, info.Force?.ColorCode ?? 50, info.Force?.FlagGlyph ?? -1, new(-13, 12));
        Text(info.Name, new(64, 42), new Color("f0f0f0"), false, info.Height == 316 ? 32 : 48);
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
        var texture = Textures[role];
        for (float y = 0; y < rect.Size.Y; y += texture.GetHeight())
        for (float x = 0; x < rect.Size.X; x += texture.GetWidth())
        {
            var size = new Vector2(Math.Min(texture.GetWidth(), rect.Size.X - x), Math.Min(texture.GetHeight(), rect.Size.Y - y));
            DrawTextureRectRegion(texture, new(rect.Position + new Vector2(x, y), size), new(Vector2.Zero, size));
        }
    }
}
