using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Tests;

public sealed class EndingPresentationTests
{
    [Fact]
    public void SceneReferencesAllowSharedCatalogEntriesAndRejectMissingCuesOrInvalidAnimation()
    {
        var image = new ImageAsset("art", "art.png", 1024, 1152);
        var map = new EndingMapPresentation("art", [], [], []);
        var definition = new EndingPresentationDefinition("art", map, map, new("art", 240, 176), [], "art", "art", "art",
            new() { ["introduction"] = new(EndingSceneKind.Opening) }, [], 0, 664, 1024, 3.2, 20, .1, .384, 2.5, 5, 2,
            "art", "art", [new(new("art", 428, -18), 8, .2)], new("art", 224, 680), 2);
        definition.Validate([image, image], ["introduction"], []);
        Assert.Throws<InvalidDataException>(() => definition.Validate([image], ["introduction", "stable-succession"], []));
        Assert.Throws<InvalidDataException>(() => (definition with { TitleAnimations = [new(new("art", 0, 0), 7, .2)] })
            .Validate([image], ["introduction"], []));
        Assert.Throws<InvalidDataException>(() => (definition with { StablePicture = "missing" }).Validate([image], ["introduction"], []));
        Assert.Throws<InvalidDataException>(() => (definition with { Copyright = definition.Copyright with { Opacity = -1 } })
            .Validate([image], ["introduction"], []));
        Assert.Throws<InvalidDataException>(() => (definition with
        { TitleAnimations = [definition.TitleAnimations[0] with { Sprite = new("art", 0, 0, float.NaN) }] })
            .Validate([image], ["introduction"], []));
    }
}
