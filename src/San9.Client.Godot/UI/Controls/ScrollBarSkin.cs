using Godot;

namespace San9.Client.Godot;

/// <summary>Presentation assets and layout, independent of the list being scrolled.</summary>
public sealed record ScrollBarSkin(TextureScope Textures, Texture2D TrackTop, Texture2D TrackMiddle, Texture2D TrackBottom,
    Texture2D ThumbTop, Texture2D ThumbMiddle, Texture2D ThumbBottom,
    Texture2D[] UpFrames, Texture2D[] DownFrames, Vector2 TrackInset)
{
    public static ScrollBarSkin FromInterface(TextureScope textures) => new(
        textures, textures.GetResource("scroll-track-top"), textures.GetResource("scroll-track-middle"), textures.GetResource("scroll-track-bottom"),
        textures.GetResource("scroll-thumb-top"), textures.GetResource("scroll-thumb-middle"), textures.GetResource("scroll-thumb-bottom"),
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures.GetResource(i == 0 ? "button-scroll-up" : $"button-scroll-up-{i}")).ToArray(),
        Enumerable.Range(0, 4).Select(i => (Texture2D)textures.GetResource(i == 0 ? "button-scroll-down" : $"button-scroll-down-{i}")).ToArray(),
        new(3, 14));

    public static ScrollBarSkin FromCatalog(TextureScope textures)
    {
        Texture2D Part(string role) => new AtlasTexture { Atlas = textures.GetResource("catalog-scroll-" + role), Region = new(0, 0, 16, textures.GetResource("catalog-scroll-" + role).GetHeight()) };
        // The source bottom ornament is 32px high; the catalog arrow slot is only 16px.
        // Keep its bottom edge inside that slot rather than overlaying the live track above it.
        Texture2D bottom = new AtlasTexture { Atlas = textures.GetResource("catalog-scroll-track-bottom"),
            Region = new(0, textures.GetResource("catalog-scroll-track-bottom").GetHeight() - 16, 16, 16) };
        Texture2D[] Frames(string role) => Enumerable.Range(0, 4).Select(i => (Texture2D)textures.GetResource("catalog-scroll-" + role + (i == 0 ? "" : $"-{i}"))).ToArray();
        return new(textures, Part("track-top"), Part("track-middle"), bottom, Part("thumb-top"), Part("thumb-middle"), Part("thumb-bottom"), Frames("up"), Frames("down"), new(0, 16));
    }

    public StyleBox CreateThumbStyle() => new ScrollThumbStyle { Top = ThumbTop, Middle = ThumbMiddle, Bottom = ThumbBottom,
        ContentMarginTop = ThumbTop.GetHeight(), ContentMarginBottom = ThumbBottom.GetHeight() };

    public static ScrollBarSkin FromEditor(TextureScope textures)
    {
        Texture2D Part(string role, int? height = null) => new AtlasTexture { Atlas = textures.GetResource("editor-scroll-" + role),
            Region = new(0, 0, 16, height ?? textures.GetResource("editor-scroll-" + role).GetHeight()) };
        Texture2D[] Frames(string role) => Enumerable.Range(0, 4).Select(i => (Texture2D)textures.GetResource("editor-scroll-" + role + (i == 0 ? "" : $"-{i}"))).ToArray();
        // Editor thumb roles are top cap (8px), repeating body (16px), bottom cap (8px).
        return new(textures, Part("track-top",12), Part("track-middle"), Part("track-bottom",12),
            Part("thumb-top"), Part("thumb-middle"), Part("thumb-bottom"), Frames("up"), Frames("down"), new(0,16));
    }
}
