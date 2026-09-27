using San9.Content.Images;

namespace San9.Client.Godot.Tests;

public sealed class TextureScopeTests
{
    [Fact]
    public void MetadataAndUnrequestedReadsNeverAcquireTextures()
    {
        // No service or engine is needed to inspect metadata or reject a missing binding.
        var scope = new TextureScope(null!, new Dictionary<string, ImageAsset>
        { ["skin"] = new("skin", "skin.png", 8, 8) });
        Assert.Equal("skin", Assert.Single(scope.Assets).Key);
        Assert.Equal(0, scope.BindingCount);
        Assert.Throws<InvalidOperationException>(() => scope["skin"]);
        Assert.False(scope.TryGetReady("skin", out _));
        Assert.Equal(0, scope.BindingCount);
    }
}
