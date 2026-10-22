using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Tests;

public sealed class DiplomacyPresentationTests
{
    [Fact]
    public void BodyAndShadowShareTheActorClockButKeepIndependentImagesAndPositions()
    {
        var body = new DiplomacySprite("body", 10, 100);
        var shadow = new DiplomacySprite("shadow", 10, 100);
        var rest = new DiplomacyLayer(body, 20, 30);
        var shade = new DiplomacyLayer(shadow, 20, 70);
        var actors = new (DiplomacyActorRole Role, bool Female, bool Ruler)[]
        {
            (DiplomacyActorRole.Ruler, false, false), (DiplomacyActorRole.Ruler, true, false),
            (DiplomacyActorRole.Adviser, false, false), (DiplomacyActorRole.Adviser, true, false),
            (DiplomacyActorRole.Envoy, false, false), (DiplomacyActorRole.Envoy, true, false),
            (DiplomacyActorRole.Envoy, false, true), (DiplomacyActorRole.Envoy, true, true),
            (DiplomacyActorRole.LeftGuard, false, false), (DiplomacyActorRole.RightGuard, false, false)
        }.Select(a => new DiplomacyActorDefinition(a.Role, a.Female, a.Ruler, rest, body, body, shade, shadow, shadow)).ToArray();
        ImageAsset[] images = [new("body", "body.png", 100, 20), new("shadow", "shadow.png", 200, 10)];
        var scene = new DiplomacyPresentationDefinition(rest, rest, [], "body", "body", "body", actors,
            "101922", .8f, "{0} {1}", "{0} {1}");
        scene.Validate(images);

        var actor = actors[0];
        foreach (var invalid in new[]
        {
            actor with { Shadow = shade with { Sprite = shadow with { Columns = 5 } } },
            actor with { SaluteShadow = shadow with { Columns = 5 } },
            actor with { BowShadow = shadow with { FrameMilliseconds = 200 } }
        })
        {
            var invalidScene = scene with { Actors = [invalid, .. actors.Skip(1)] };
            Assert.Throws<InvalidDataException>(() => invalidScene.Validate(images));
        }
    }
}
