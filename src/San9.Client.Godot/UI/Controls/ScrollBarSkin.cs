using Godot;

namespace San9.Client.Godot;

/// <summary>Presentation assets and layout, independent of the list being scrolled.</summary>
public sealed record ScrollBarSkin(Texture2D TrackTop, Texture2D TrackMiddle, Texture2D TrackBottom,
    Texture2D ThumbTop, Texture2D ThumbMiddle, Texture2D ThumbBottom,
    Texture2D[] UpFrames, Texture2D[] DownFrames, Vector2 TrackInset)
{
    public static ScrollBarSkin FromInterface(TextureScope textures) => new(
        textures["scroll-track-top"], textures["scroll-track-middle"], textures["scroll-track-bottom"],
        textures["scroll-thumb-top"], textures["scroll-thumb-middle"], textures["scroll-thumb-bottom"],
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-up" : $"button-scroll-up-{i}"]).ToArray(),
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-down" : $"button-scroll-down-{i}"]).ToArray(),
        new(3, 14));

    public static ScrollBarSkin FromCatalog(TextureScope textures)
    {
        Texture2D Part(string role) => new AtlasTexture { Atlas = textures["catalog-scroll-" + role], Region = new(0, 0, 16, textures["catalog-scroll-" + role].GetHeight()) };
        // The source bottom ornament is 32px high; the catalog arrow slot is only 16px.
        // Keep its bottom edge inside that slot rather than overlaying the live track above it.
        Texture2D bottom = new AtlasTexture { Atlas = textures["catalog-scroll-track-bottom"],
            Region = new(0, textures["catalog-scroll-track-bottom"].GetHeight() - 16, 16, 16) };
        Texture2D[] Frames(string role) => Enumerable.Range(0, 4).Select(i => (Texture2D)textures["catalog-scroll-" + role + (i == 0 ? "" : $"-{i}")]).ToArray();
        return new(Part("track-top"), Part("track-middle"), bottom, Part("thumb-top"), Part("thumb-middle"), Part("thumb-bottom"), Frames("up"), Frames("down"), new(0, 16));
    }

    public StyleBox CreateThumbStyle() => new ScrollThumbStyle { Top = ThumbTop, Middle = ThumbMiddle, Bottom = ThumbBottom,
        ContentMarginTop = ThumbTop.GetHeight(), ContentMarginBottom = ThumbBottom.GetHeight() };
}
