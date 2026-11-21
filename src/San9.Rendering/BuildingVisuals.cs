using San9.Content.Maps;

namespace San9.Rendering;

public sealed record BuildingVisualState(int Id, int Durability, int Appearance, int Development, uint Soldiers, int Color, int Glyph)
{
    public bool IsDestroyed { get; init; }
    public MapBuildingPlacement? Placement { get; init; }
    public bool IsEngaged { get; init; }
    public bool IsUnderConstruction { get; init; }
    public int Morale { get; init; }
    public int Month { get; init; } = 1;
}
public sealed record BuildingVisuals(MapBuilding[] Buildings, MapAnimatedSprite[] Animations)
{
    /// <summary>Cursor-local body layers of the completed facility, without garrison, flag or ground effects.</summary>
    public static MapSprite[] ConstructionPreview(MapPackage map, int type)
    {
        int maximum = map.BuildingTypes.Single(t=>t.Id==type).MaxDurability;
        var composition = map.Compositions.Single(c=>c.Id==BuildingAppearance.CompositionId(type,0,maximum,maximum));
        return composition.Parts.Where(p=>!p.PaletteAlpha && !p.Ground)
            .Select(p=>new MapSprite($"building-{p.ResourceId}-key",p.X,p.Y)).ToArray();
    }
    public static BuildingVisuals Resolve(MapPackage map, IReadOnlyList<BuildingVisualState> states)
    {
        var input = states.ToDictionary(s => s.Id);
        var compositions = map.Compositions.ToDictionary(c => c.Id);
        var types = map.BuildingTypes.ToDictionary(t => t.Id);
        var clips = map.Animations.Clips.ToDictionary(c => c.Id);
        List<MapBuilding> buildings = []; List<MapAnimatedSprite> animations = [];
        foreach (var b in states.Select(s => s.Placement ?? map.Buildings.Single(b => b.Id == s.Id)).OrderBy(b => b.X+b.Y).ThenBy(b => b.X))
        {
            var state = input[b.Id]; int type = b.FixedType, maximum = types[type].MaxDurability;
            if (state.IsDestroyed) continue;
            if (state.Color is < 0 or > 50 || state.Glyph is < -1 or > 185 || state.Durability < 0 || maximum <= 0)
                throw new InvalidDataException("Invalid runtime building visual state.");
            int id = BuildingAppearance.CompositionId(type, state.Appearance, state.Durability, maximum, state.IsUnderConstruction);
            if (!compositions.TryGetValue(id,out var composition)) throw new InvalidDataException($"Unconfirmed composition {id}.");
            var center = MapGeometry.Center(b.X,b.Y, map.Height); int x = center.X, y = center.Y-10*(map.Squares[b.Y*map.Width+b.X].Elevation-1);
            var sprites = BuildingAppearance.Sprites(composition, type == 0, state.Development, x, y);
            buildings.Add(new(b.Id,b.X,b.Y,type,id,sprites));
            if (state.Soldiers > 0)
                for (int i=0;i<composition.Guards.Length;i++)
                {
                    var g=composition.Guards[i]; animations.Add(new($"soldier-{g.Direction}",x+g.X,y+g.Y,(b.Id+i)%8,b.Id) { Depth = y + g.DepthOffset + 30000 });
                }
            for (int i=0;i<composition.Flags.Length;i++)
            {
                var f=composition.Flags[i]; int phase=(b.Id+i)%4;
                animations.Add(new("flag-pole-back",x+f.X,y+f.Y,phase,b.Id) { Depth = y + f.Y + 30100 });
                animations.Add(new($"flag-cloth-{state.Color}",x+f.X,y+f.Y,phase,b.Id) { Depth = y + f.Y + 30100 });
                if(state.Glyph>=0) animations.Add(new($"flag-glyph-{FlagVisuals.GlyphInk(state.Color)}-{state.Glyph}",x+f.X,y+f.Y,phase,b.Id) { Depth = y + f.Y + 30100 });
                animations.Add(new("flag-pole-front",x+f.X,y+f.Y,phase,b.Id) { Depth = y + f.Y + 30100 });
            }
            var smoke = BuildingSmokeVisuals.Resolve(composition.Smoke, state, maximum, type == 0, clips);
            animations.AddRange(smoke.Select(s => s with { X = x + s.X, Y = y + s.Y, Depth = s.BehindBuilding ? sprites.Min(p => p.Depth) - 1 : sprites.Max(p => p.Depth) + 1 }));
        }
        return new(buildings.ToArray(),animations.ToArray());
    }
}
