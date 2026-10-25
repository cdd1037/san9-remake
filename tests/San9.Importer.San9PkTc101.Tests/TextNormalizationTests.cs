using System.Text;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class TextNormalizationTests
{
    [Theory]
    [InlineData(5207, "建議校尉", "建義校尉", "建义校尉")]
    [InlineData(1464, "孫濱兵法", "孫臏兵法", "孙膑兵法")]
    [InlineData(1520, "博奕論", "博弈論", "博弈论")]
    [InlineData(214, "壼關", "壺關", "壶关")]
    [InlineData(434, "末蘆", "末盧", "末卢")]
    [InlineData(436, "烏鼠山", "鳥鼠山", "鸟鼠山")]
    [InlineData(1679 + 4 * 527, "彤", "肜", "肜")]
    [InlineData(1678 + 4 * 645, "泠", "冷", "冷")]
    [InlineData(1680 + 4 * 395, "儁艾", "儁乂", "儁乂")]
    [InlineData(1680 + 4 * 416, "翼德", "益德", "益德")]
    [InlineData(1679 + 4 * 348, "乾", "乾", "乾")]
    [InlineData(1678 + 4 * 120, "麴", "麴", "麹")]
    [InlineData(6097, "進行人材的移動與挖掘。", "進行人材的移動與挖掘。", "进行人材的移动与挖掘。")]
    public void DecodingEditorialSourceAndTargetScriptAreSeparate(int message, string original, string canonical, string simplified)
    {
        byte[] bytes = Encode(original);
        var converter = new OriginalTextConverter();
        Assert.Equal(original, OriginalTextDecoder.Decode(bytes));
        Assert.Equal(canonical, converter.Canonicalize(bytes, message));
        Assert.Equal(simplified, converter.Convert(bytes, message));
    }

    [Fact]
    public void PreservesTheSevenConfirmedNameCharactersAndSupplementarySourceGlyph()
    {
        var converter = new OriginalTextConverter();
        foreach (var (person, original) in new[] { (67, "綝"), (247, "顗"), (292, "子勑"), (522, "紞"),
                     (602, "璝"), (689, "雲騄"), (239, "儁"), (368, "𩅦") })
        {
            int message = (person == 292 ? 1680 : 1679) + 4 * person;
            Assert.Equal(original.Replace("雲", "云", StringComparison.Ordinal), converter.Convert(Encode(original), message));
        }
    }

    [Theory]
    [InlineData(7671, "孫武所\n著的兵法書。", "孙武所\n著的兵法书。")]
    [InlineData(7704, "曹丕的著\n作。", "曹丕的著\n作。")]
    public void AuthorshipSurvivesLayoutBreaksWithoutChangingAspectParticles(int message, string original, string expected)
    {
        var converter = new OriginalTextConverter();
        Assert.Equal(original, converter.Canonicalize(Encode(original), message));
        Assert.Equal(expected, converter.Convert(Encode(original), message));
        Assert.Equal("装饰着、记载着。", converter.Convert(Encode("裝飾著、記載著。")));
    }

    [Fact]
    public void SunBinDescriptionReversesTheAliasAndPreservesItsParagraphBreaks()
    {
        const string original = "戰國時代，齊國兵法家\n孫濱所著的兵法書。孫\n濱亦作「孫臏」，據說\n孫濱是孫武的子孫。";
        const string expected = "战国时代，齐国兵法家\n孙膑所著的兵法书。孙\n膑亦作「孙滨」，据说\n孙膑是孙武的子孙。";
        Assert.Equal(expected, new OriginalTextConverter().Convert(Encode(original), 7677));
    }

    [Fact]
    public void LiteralNamesInNarrativesAndTheSourceNamePoolKeepTheirChosenCharacters()
    {
        var converter = new OriginalTextConverter();
        Assert.Equal("孙\n綝、荀顗、朱儁、孙乾、钟繇、麹义。",
            converter.Convert(Encode("孫\n綝、荀顗、朱儁、孫乾、鍾繇、麴義。"), 0));
        Assert.Equal("顗", converter.Convert(Encode("顗"), 11746));
        Assert.Equal("綝", converter.Convert(Encode("綝"), 11974));
        Assert.Equal("负责诸\n殿门", converter.Convert(Encode("負責庶\n殿門"), 7836));
        Assert.Equal("生平所著「青囊书」", converter.Convert(Encode("生平所著「青囊書」"), 497, "biographies"));
    }

    [Fact]
    public void EditorialWordsDoNotAlterMessageBindingsOrBranchStructure()
    {
        byte[] bytes = [2, 1, 0xDA, .. Encode("驃駻"), 5, 5, 5];
        var parsed = new OriginalMessageParser().Parse(bytes, messageId: 7122);
        Assert.True(parsed.FullyParsed && parsed.SemanticsConfirmed);
        var styled = Assert.IsType<MessageStyled>(parsed.Definition.Nodes[0]);
        Assert.Equal("person.0.name", Assert.IsType<MessageValue>(Assert.Single(styled.Body)).Expression.Binding);
        Assert.Equal("剽悍", Assert.IsType<MessageLiteral>(parsed.Definition.Nodes[1]).BbCode);
        Assert.IsType<MessageReturn>(parsed.Definition.Nodes[2]);
    }

    [Fact]
    public void RejectsChangedSourcesAndDoesNotLeakRulesToOtherResources()
    {
        var converter = new OriginalTextConverter();
        Assert.Throws<InvalidDataException>(() => converter.Convert(Encode("建義校尉"), 5207));
        Assert.Equal("建议校尉", converter.Convert(Encode("建議校尉"), 0));
        Assert.Equal("蔣幹 高幹", converter.Canonicalize(Encode("蔣幹 高幹")));
        Assert.Equal("蒋干 高干", converter.Convert(Encode("蔣幹 高幹")));
        Assert.Equal("乾", converter.Convert(Encode("乾"), 1611 + 4 * 348, "base-messages"));
        Assert.Throws<InvalidDataException>(() => OriginalTextEditorial.ValidatePersonReferences(348, 348, 349, 348));
    }

    [Theory]
    [InlineData("建議", "校尉")]
    [InlineData("", "建議校尉")]
    [InlineData("建議校尉", "")]
    public void WholeEntryCorrectionsRejectTextSeparatedByMessageReturns(string before, string after)
    {
        byte[] bytes = [.. Encode(before), 5, 5, 5, .. Encode(after), 5, 5, 5];
        var parsed = new OriginalMessageParser().Parse(bytes, messageId: 5207);
        Assert.True(parsed.FullyParsed);
        Assert.False(parsed.SemanticsConfirmed);
        Assert.Contains(parsed.Issues, issue => issue.Reason.Contains("Editorial source mismatch", StringComparison.Ordinal));
    }

    private static byte[] Encode(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var extensions = new Dictionary<string, byte> { ["伷"] = 0x40, ["璝"] = 0x41, ["寗"] = 0x42,
            ["勑"] = 0x43, ["𩅦"] = 0x44, ["儁"] = 0x45 };
        List<byte> result = [];
        foreach (var rune in text.EnumerateRunes())
            if (extensions.TryGetValue(rune.ToString(), out byte code)) result.AddRange([0xFA, code]);
            else result.AddRange(encoding.GetBytes(rune.ToString()));
        return result.ToArray();
    }
}
