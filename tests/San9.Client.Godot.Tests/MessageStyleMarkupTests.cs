using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MessageStyleMarkupTests
{
    [Theory]
    [InlineData("blue", "7EBAC2")]
    [InlineData("green", "A4D168")]
    [InlineData("peach", "F0B080")]
    [InlineData("yellow", "F8F8B0")]
    public void ForegroundPresentationChangesEmphasisAndRestoresSurroundingText(string color, string foreground)
    {
        string markup = $"before[preset={color}-outline]name[/preset]after";
        var result = MessageStyleMarkup.Expand(markup, MessageEmphasisMode.Foreground);
        Assert.Equal($"before[color=#{foreground}][outline_size=4][outline_color=#00000091]name" +
            "[/outline_color][/outline_size][/color]after", result.BbCode);
        Assert.DoesNotContain("[color=", MessageStyleMarkup.Expand(markup).BbCode);
    }

    [Fact]
    public void ForegroundPresentationPreservesNestedScopesAndExplicitForeground()
    {
        var result = MessageStyleMarkup.Expand("[preset=yellow]A[preset=blue-outline]B[/preset]C[/preset]D",
            MessageEmphasisMode.Foreground);
        Assert.Equal("[color=#F8F8B0][outline_size=4][outline_color=#00000091]A" +
            "[color=#7EBAC2][outline_size=4][outline_color=#00000091]B[/outline_color][/outline_size][/color]" +
            "C[/outline_color][/outline_size][/color]D", result.BbCode);
    }

    [Fact]
    public void NestedPresetsAreBalancedAndEscapedParametersCannotCreateStyles()
    {
        var result = MessageStyleMarkup.Expand("[preset=yellow]A[preset=blue-outline]B[/preset]C[/preset][lb]preset=green]D");
        Assert.True(result.HasPresets);
        Assert.Equal("[color=#F8F8B0][outline_size=4][outline_color=#00000091]A" +
            "[outline_size=4][outline_color=#98B0F891]B[/outline_color][/outline_size]" +
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
