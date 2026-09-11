using Godot;

namespace San9.Client.Godot;

/// <summary>Presentation assets and layout, independent of the list being scrolled.</summary>
public sealed record ScrollBarSkin(Texture2D TrackTop, Texture2D TrackMiddle, Texture2D TrackBottom,
    Texture2D ThumbTop, Texture2D ThumbMiddle, Texture2D ThumbBottom,
    Texture2D[] UpFrames, Texture2D[] DownFrames, Vector2 TrackInset)
{
    public static ScrollBarSkin FromInterface(IReadOnlyDictionary<string, Texture2D> textures) => new(
        textures["scroll-track-top"], textures["scroll-track-middle"], textures["scroll-track-bottom"],
        textures["scroll-thumb-top"], textures["scroll-thumb-middle"], textures["scroll-thumb-bottom"],
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-up" : $"button-scroll-up-{i}"]).ToArray(),
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures[i == 0 ? "button-scroll-down" : $"button-scroll-down-{i}"]).ToArray(),
        new(3, 14));

    public static ScrollBarSkin FromCatalog(IReadOnlyDictionary<string, Texture2D> textures)
    {
        Texture2D Part(string role) => new AtlasTexture { Atlas = textures["catalog-scroll-" + role], Region = new(0, 0, 16, textures["catalog-scroll-" + role].GetHeight()) };
        Texture2D[] Frames(string role) => Enumerable.Range(0, 4).Select(i => (Texture2D)textures["catalog-scroll-" + role + (i == 0 ? "" : $"-{i}")]).ToArray();
        return new(Part("track-top"), Part("track-middle"), Part("track-bottom"), Part("thumb-top"), Part("thumb-middle"), Part("thumb-bottom"), Frames("up"), Frames("down"), new(0, 16));
    }

    public StyleBox CreateThumbStyle() => new ScrollThumbStyle { Top = ThumbTop, Middle = ThumbMiddle, Bottom = ThumbBottom,
        ContentMarginTop = ThumbTop.GetHeight(), ContentMarginBottom = ThumbBottom.GetHeight() };
}
