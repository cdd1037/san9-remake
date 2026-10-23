using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Tests;

public sealed class StartMenuPresentationTests
{
    [Fact]
    public void ValidateRejectsBrokenAnimationResourcesAndControlIdentities()
    {
        ImageAsset[] images = [new("button", "button.png", 160, 40), new("sky", "sky.png", 1024, 432),
            new("other-sky", "other.png", 1024, 400)];
        var button = new StartMenuSprite("button", 0, 0, 4);
        var animation = new StartMenuAnimation(["sky"], 1, 0, 0, .2);
        var entry = new StartMenuEntry("new-game", "开始", "开始游戏。", button, animation, []);
        var scene = new StartMenuPresentation([], [animation], [], button, button, "请选择。", [entry]);
        scene.Validate(images);
        foreach (var invalid in new[]
        {
            scene with { Entries = [entry, entry] },
            scene with { Entries = [entry with { Illustration = animation with { Frames = ["missing"] } }] },
            scene with { Ambience = [animation with { Frames = [] }] },
            scene with { Ambience = [animation with { Frames = ["sky", "other-sky"] }] },
            scene with { Ambience = [animation with { Columns = 3 }] },
            scene with { Ambience = [animation with { FrameSeconds = double.NaN }] },
            scene with { Ambience = [animation with { Opacity = 2 }] }
        })
            Assert.Throws<InvalidDataException>(() => invalid.Validate(images));
    }
}
