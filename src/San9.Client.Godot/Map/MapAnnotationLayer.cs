using Godot;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Published layout is shared by drawing and picking, independent of draw callbacks.</summary>
internal sealed class MapAnnotationLayer
{
    private sealed record Entry(MapObjectTarget Target, Vector2 Position, string Name, uint Soldiers,
        int Morale, int Durability, int MaxDurability, string[] Icons, Rect2? HitBounds);
    private Entry[] _entries = [];
    public bool HasAnimatedBars { get; private set; }
    public void Prepare(MapSceneView.Frame frame, Font font, TextCatalog texts)
    {
        List<Entry> entries = [];
        foreach (var building in frame.Region.Buildings)
        {
            var value = frame.Region.Scene.Settlements[building.Id];
            var position = frame.Region.Scene.Anchor(building);
            if (!frame.Region.Bounds.Intersects(new Rect2(position, new(116, 55)))) continue;
            entries.Add(new(new(building.Id, false), position, value.ResolveName(texts), value.Soldiers,
                value.Morale, value.Durability, value.MaxDurability, value.Icons.ToArray(), null));
        }
        var units = frame.Region.Scene.State.World.Units.ToDictionary(u => u.Id);
        int fontSize = GameTextStyles.Building(false).FontSize;
        foreach (var unit in frame.Region.Units.Where(u => frame.Region.Scene.IsUnitVisible(u, frame.ViewportBounds)).Select(frame.Region.Scene.Sample))
        {
            string name = unit.CommanderNameKey is { } key ? texts.Resolve(key) + "队" : "部队";
            float width = Math.Max(76, font.GetStringSize(name, fontSize: fontSize).X);
            var bounds = new Rect2(unit.LabelX, unit.LabelY, width, 47 + font.GetDescent(fontSize));
            if (!frame.Region.Bounds.Intersects(bounds)) continue;
            entries.Add(new(new(unit.Id, true), new(unit.LabelX, unit.LabelY), name, unit.Soldiers,
                unit.Morale, 0, 0, frame.Region.Scene.State.UnitPurpose(units[unit.Id]) switch
                {
                    San9.Core.Domain.UnitPurpose.Construction => ["construction"],
                    San9.Core.Domain.UnitPurpose.Transport => ["transport"],
                    _ => []
                }, bounds));
        }
        _entries = entries.ToArray();
        HasAnimatedBars = _entries.Any(e => Animated(e.Morale, 100) || !e.Target.IsUnit && Animated(e.Durability, e.MaxDurability));
        static bool Animated(int value, int maximum) => BuildingInfoGeometry.FillWidth(value, maximum) > 0 &&
            BuildingInfoGeometry.FillState(value, maximum) != "normal";
    }
    public MapObjectTarget? Hit(Vector2 point)
    {
        for (int i = _entries.Length - 1; i >= 0; i--)
            if (_entries[i].HitBounds is { } bounds && bounds.HasPoint(point)) return _entries[i].Target;
        return null;
    }
    public void Draw(Node2D target, Font font, TextureScope textures, MapObjectTarget? hovered,
        Func<Vector2, Vector2> project, float zoom, double barTime)
    {
        foreach (var entry in _entries)
        {
            target.DrawSetTransform(project(entry.Position), 0, Vector2.One * zoom);
            var style = GameTextStyles.Building(hovered == entry.Target);
            int soldiersY = entry.Target.IsUnit ? 20 : 16;
            GameTextPresenter.Draw(target, font, entry.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new(0, soldiersY), style, HorizontalAlignment.Right, 72);
            if (!entry.Target.IsUnit) Bar("durability", entry.Durability, entry.MaxDurability, new(0, 16), entry.Target.Id);
            Bar("morale", entry.Morale, 100, new(0, entry.Target.IsUnit ? 20 : 29), entry.Target.Id);
            GameTextPresenter.Draw(target, font, entry.Name, new(0, entry.Target.IsUnit ? 47 : 53), style);
            foreach (string icon in entry.Icons) target.DrawTexture(textures["icon-" + icon], entry.Target.IsUnit ? new(2, 0) : icon == "emperor" ? new(96, 17) : new(75, 17));
        }
        target.DrawSetTransform(Vector2.Zero);
        void Bar(string kind, int current, int maximum, Vector2 point, int objectId)
        {
            target.DrawTexture(textures[$"bar-{kind}-frame"], point);
            int width = BuildingInfoGeometry.FillWidth(current, maximum);
            if (width == 0) return;

            var blend = MapInfoBarAnimation.Sample(current, maximum, barTime, objectId);
            Layer("normal", blend.Normal);
            Layer("warning", blend.Warning);
            Layer("critical", blend.Critical);
            void Layer(string state, float opacity)
            {
                if (opacity <= 0) return;
                var fill = textures[$"bar-{kind}-{state}"];
                target.DrawTextureRectRegion(fill, new(point + new Vector2(4, 4), new Vector2(width, fill.GetHeight())),
                    new(0, 0, width, fill.GetHeight()), new Color(1, 1, 1, opacity));
            }
        }
    }
}
