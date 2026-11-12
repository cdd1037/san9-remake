namespace San9.Content.Maps;

/// <summary>Shared building composition and body geometry for import-time bounds and runtime drawing.</summary>
public static class BuildingAppearance
{
    private static readonly int[] Bases = [0, 15, 19, 24, 29, 34, 39, 43, 48, 53, 58, 62, 66, 71, 76, 81, 86];
    public static int CompositionType(int id) => Enumerable.Range(0, Bases.Length).Last(i => Bases[i] <= id);
    public static int CompositionId(int type, int appearance, int durability, int maximum)
    {
        if ((uint)type >= Bases.Length || maximum <= 0 || durability < 0) throw new InvalidDataException("Invalid building appearance input.");
        bool four = type is 0 or 2 or 3 or 4 or 5 or 7 or 9 or 12 or 13 or 16;
        int frame = (long)durability*100 < maximum*20L ? 0 : four && (long)durability*100 < maximum*40L ? 1 :
            (long)durability*100 < maximum*60L ? (four?2:1) : (four?3:2);
        int style = type != 0 ? 0 : appearance switch { 0 => 1, 1 => 2, 2 => 0, _ => throw new InvalidDataException("Invalid city style.") };
        return Bases[type]+1+frame+(type==0?style*5:0);
    }
    public static MapSprite[] Sprites(MapComposition composition, bool city, int development, int x, int y)
    {
        int variant = development <= 300 ? 0 : development <= 600 ? 1 : 2;
        return composition.Parts.Select(part =>
        {
            int resource = part.ResourceId + (city && part.DevelopmentVariant ? variant : 0);
            string transparency = part.PaletteAlpha ? "alpha" : "key";
            int depth = part.PaletteAlpha && !part.DevelopmentVariant ? y + part.Y + 10000
                : part.Ground && !part.DevelopmentVariant && !part.PaletteAlpha ? 1
                : y + part.Y + part.DepthOffset + (part.DevelopmentVariant && !part.PaletteAlpha ? 29990 : 30000);
            return new MapSprite($"building-{resource}-{transparency}", x + part.X, y + part.Y) { Depth = depth };
        }).ToArray();
    }
}
