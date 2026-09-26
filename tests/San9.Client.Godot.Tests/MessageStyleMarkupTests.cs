using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MessageStyleMarkupTests
{
    [Fact]
    public void NestedPresetsAreBalancedAndEscapedParametersCannotCreateStyles()
    {
        var result = MessageStyleMarkup.Expand("[preset=yellow]A[preset=blue-outline]B[/preset]C[/preset][lb]preset=green]D");
        Assert.True(result.HasPresets);
        Assert.Equal("[color=#F8F8B0][outline_size=1][outline_color=#000000A6]A" +
            "[color=#F0F0F0][outline_size=1][outline_color=#98B0F8A6]B[/outline_color][/outline_size][/color]" +
            "C[/outline_color][/outline_size][/color][lb]preset=green]D", result.BbCode);
        Assert.False(MessageStyleMarkup.Expand("plain[lb]preset=yellow]").HasPresets);
    }

    [Theory]
    [InlineData("[preset=unknown]A[/preset]")]
    [InlineData("[preset=yellow]A")]
    [InlineData("A[/preset]")]
    public void InvalidScopesFailBeforeRendering(string markup) =>
        Assert.Throws<InvalidDataException>(() => MessageStyleMarkup.Expand(markup));
}
