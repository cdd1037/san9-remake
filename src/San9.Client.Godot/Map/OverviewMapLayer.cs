using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Only ownership colors change after the session geography has been prepared.</summary>
public sealed class OverviewMapLayer(TextureService service, InterfaceSkin skin) : IDisposable
{
    private ImageTexture? _territories;
    private readonly Vector2[][] _borders = service.OverviewMap.Borders.Select(path =>
        path.Points.Select(p => new Vector2((float)p.X * 800, (float)p.Y * 400)).ToArray()).ToArray();
    private Dictionary<int, OverviewColor> _colors = [];
    public MinimapGeometry Geometry => service.OverviewMap.Geometry;
    public Vector2 Point(double x, double y)
    {
        var p = Geometry.FromGrid(x, y);
        return new((float)p.X * 800, (float)p.Y * 400);
    }

    public void Update(StrategySnapshot view)
    {
        var forces = view.Forces.ToDictionary(f => f.Id, f => f.ColorCode);
        var colors = view.Settlements.Where(s => s.Kind == SettlementKind.City).ToDictionary(s => s.Id, s =>
        {
            int code = s.OwnerId is int owner ? forces[owner] : 50;
            var color = new Color(skin.Colors[$"overview-force-{code}"]);
            return new OverviewColor((byte)color.R8, (byte)color.G8, (byte)color.B8);
        });
        if (_territories is not null && colors.Count == _colors.Count && colors.All(p => _colors.TryGetValue(p.Key, out var color) && color == p.Value)) return;
        var pixels = service.OverviewMap.Territories(colors);
        using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Pixels);
        if (_territories is null) _territories = ImageTexture.CreateFromImage(image);
        else _territories.Update(image);
        _colors = colors;
    }

    public void Draw(CanvasItem owner, TextureScope textures, Vector2 origin, bool showTerritories = true)
    {
        owner.DrawTexture(textures["overview-terrain"], origin);
        if (!showTerritories || _territories is null) return;
        DrawTerritories(owner, origin);
        DrawBorders(owner, origin);
    }

    public void DrawTerritories(CanvasItem owner, Vector2 origin)
    {
        if (_territories is not null) owner.DrawTexture(_territories, origin);
    }

    public void DrawBorders(CanvasItem owner, Vector2 origin)
    {
        owner.DrawSetTransform(origin);
        foreach (var border in _borders)
            owner.DrawPolyline(border, new Color(0, 0, 0, 143 / 255f), 1.5f, true);
        owner.DrawSetTransform(Vector2.Zero);
    }

    public void Dispose() => _territories?.Dispose();
}
