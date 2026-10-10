using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class RichTextPageMarkupTests
{
    private const string Styled = "[color=green]甲乙[outline_size=2]丙丁[/outline_size]戊己[/color]庚";
    private const string Symbols = "[font_size=24]😀[lb]甲[img width=24]res://icon.tres[/img][rb]乙[/font_size]";
    private const string Lines = "[color=green]甲\n\n乙\n丙[/color]";

    [Theory]
    [InlineData(Styled, 3, 5, "[color=green][outline_size=2]丁[/outline_size]戊[/color]")]
    [InlineData(Styled, 5, 7, "[color=green][outline_size=2][/outline_size]己[/color]庚")]
    [InlineData(Styled, 0, 1, "[color=green]甲[outline_size=2][/outline_size][/color]")]
    [InlineData(Symbols, 3, 5, "[font_size=24][img width=24]res://icon.tres[/img][rb][/font_size]")]
    [InlineData(Symbols, 0, 2, "[font_size=24]😀[lb][/font_size]")]
    [InlineData(Lines, 1, 4, "[color=green]\n\n乙[/color]")]
    [InlineData(Lines, 4, 4, "")]
    public void SlicePreservesStylesAndCountsOnlyVisibleUnicodeScalars(string document, int start, int end, string expected) =>
        Assert.Equal(expected, RichTextPageMarkup.Slice(document, start, end));
}
