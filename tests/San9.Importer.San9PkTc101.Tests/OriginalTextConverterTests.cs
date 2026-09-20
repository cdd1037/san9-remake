using System.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OriginalTextConverterTests
{
    [Fact]
    public void ExtendedCodesAreDecodedAnywhereWithoutNameMatching()
    {
        var converter = new OriginalTextConverter();
        var entry = converter.Convert([65, 0xFA, 0x40, 10, 0xFA, 0x41, 0xFA, 0x45, 0xFA, 0x40]);
        Assert.Equal("寗勑𩅦", converter.Convert([0xFA, 0x42, 0xFA, 0x43, 0xFA, 0x44]).SourceText);
        Assert.Equal("A伷\n璝儁伷", entry.SourceText);
        Assert.Equal("A伷\n𪻺俊伷", entry.Text);
    }

    [Theory]
    [InlineData("劉", "刘")]
    [InlineData("魏續 高順 胡車兒 胡軫 徐榮 宋憲", "魏续 高顺 胡车儿 胡轸 徐荣 宋宪")]
    [InlineData("乾隆 乾坤 乾燥", "乾隆 乾坤 干燥")]
    public void PreservesDecodedSourceAndConvertsDisplay(string source, string expected)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var entry = new OriginalTextConverter().Convert(Encoding.GetEncoding(950).GetBytes(source));
        Assert.Equal(source, entry.SourceText);
        Assert.Equal(expected, entry.Text);
    }

    [Fact]
    public void RejectsUnknownGlyphsTruncationAndScriptControls()
    {
        var converter = new OriginalTextConverter();
        Assert.Throws<InvalidDataException>(() => converter.Convert([0xFA, 0x46]));
        Assert.Equal("[img]icons/city[/img]", converter.Convert([0xFA, 0xD0]).Text);
        Assert.Throws<InvalidDataException>(() => converter.Convert([0xFA]));
        Assert.Throws<InvalidDataException>(() => converter.Convert([1, 2]));
    }
}
