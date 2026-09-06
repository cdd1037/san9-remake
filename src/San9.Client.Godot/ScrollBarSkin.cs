using Godot;

namespace San9.Client.Godot;

/// <summary>Presentation assets and layout, independent of the list being scrolled.</summary>
public sealed record ScrollBarSkin(Texture2D TrackTop, Texture2D TrackMiddle, Texture2D TrackBottom,
    Texture2D ThumbTop, Texture2D ThumbMiddle, Texture2D ThumbBottom,
    Texture2D[] UpFrames, Texture2D[] DownFrames, Vector2 TrackInset)
{
    public static ScrollBarSkin FromInterface(IReadOnlyDictionary<string, ImageTexture> textures) => new(
        textures["scroll-track-top"], textures["scroll-track-middle"], textures["scroll-track-bottom"],
        textures["scroll-thumb-top"], textures["scroll-thumb-middle"], textures["scroll-thumb-bottom"],
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-up" : $"button-scroll-up-{i}"]).ToArray(),
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-down" : $"button-scroll-down-{i}"]).ToArray(),
        new(3, 14));

    public static ScrollBarSkin FromCatalog(IReadOnlyDictionary<string, ImageTexture> textures)
    {
        Texture2D Part(string role) => new AtlasTexture { Atlas = textures["catalog-scroll-" + role], Region = new(0, 0, 16, textures["catalog-scroll-" + role].GetHeight()) };
        Texture2D[] Frames(string role) => Enumerable.Range(0, 4).Select(i => (Texture2D)textures["catalog-scroll-" + role + (i == 0 ? "" : $"-{i}")]).ToArray();
        return new(Part("track-top"), Part("track-middle"), Part("track-bottom"), Part("thumb-top"), Part("thumb-middle"), Part("thumb-bottom"), Frames("up"), Frames("down"), new(0, 16));
    }

    public StyleBoxTexture CreateThumbStyle()
    {
        int width = ThumbMiddle.GetWidth(), top = ThumbTop.GetHeight(), bottom = ThumbBottom.GetHeight();
        int period = ThumbMiddle.GetHeight();
        using var image = Image.CreateEmpty(width, top + period + bottom, false, Image.Format.Rgba8);
        using var middleImage = ThumbMiddle.GetImage();
        using var topImage = ThumbTop.GetImage();
        using var bottomImage = ThumbBottom.GetImage();
        // The original tiles the center from the whole surface origin before overlaying its caps.
        for (int y = 0; y < period; y++)
            image.BlitRect(middleImage, new(0, (top + y) % period, width, 1), new(0, top + y));
        image.BlitRect(topImage, new(0, 0, width, top), Vector2I.Zero);
        image.BlitRect(bottomImage, new(0, 0, width, bottom), new(0, top + period));
        var style = new StyleBoxTexture { Texture = ImageTexture.CreateFromImage(image),
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        style.SetTextureMargin(Side.Top, top); style.SetTextureMargin(Side.Bottom, bottom);
        style.ContentMarginTop = top; style.ContentMarginBottom = bottom;
        return style;
    }
}
