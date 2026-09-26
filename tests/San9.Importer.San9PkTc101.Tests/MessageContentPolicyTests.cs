using System.Text;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MessageContentPolicyTests
{
    [Fact]
    public void AppearanceHelpUsesFixedCountsAndForegroundColorWithoutChangingSourceParsing()
    {
        const string key = "messages.8677", catalog = "messages";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // 8C is a source control byte, not a CP950 character.
        byte[] raw = [.. Encoding.GetEncoding(950).GetBytes("男性容貌有"), 2, 0x8c, .. Encoding.ASCII.GetBytes("14b\x1bH70"),
            2, 0x8c, .. Encoding.ASCII.GetBytes("32b"), .. Encoding.GetEncoding(950).GetBytes("種，\n女性容貌有"),
            2, 0x8c, .. Encoding.ASCII.GetBytes("14b\x1bH30"), 2, 0x8c, .. Encoding.ASCII.GetBytes("32b"),
            .. Encoding.GetEncoding(950).GetBytes("種。"), 5, 5, 5];
        var original = new OriginalMessageParser().Parse(raw, catalog);
        Assert.False(original.Definition.SemanticsConfirmed);
        var messages = new Dictionary<string, MessageDefinition> { [key] = original.Definition };
        var audit = MessageContentPolicy.Apply(key, raw, catalog, messages);
        Assert.NotNull(audit);
        Assert.Contains("[color=#F8F8B0]70[/color]种", MessageMarkup.Static(messages[key].Nodes.TakeWhile(n => n is not MessageReturn)));
        Assert.Contains("[color=#F8F8B0]30[/color]種", MessageMarkup.Static(messages[key].Nodes.TakeWhile(n => n is not MessageReturn), source: true));
        Assert.True(MessageGraph.Requirements(key, messages).Executable);
        Assert.False(new OriginalMessageParser().Parse(raw, catalog).Definition.SemanticsConfirmed);
        raw[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => MessageContentPolicy.Apply(key, raw, catalog, messages));
    }

    [Fact]
    public void ReviewedTailPreservesRandomBranchesAndUnblocksParent()
    {
        byte[] body = [1, (byte)'S', 2, 0xca, (byte)'%', (byte)'2', (byte)'A', 5, 5, 5, (byte)'B', 5, 5, 5];
        byte[] raw = [.. body, 1, (byte)'J'];
        var source = new OriginalMessageParser().Parse(raw, "messages");
        var messages = new Dictionary<string, MessageDefinition>
        {
            ["messages.12712"] = source.Definition,
            ["parent"] = new([new MessageReference("messages.12712", true)], true, [])
        };
        Assert.False(MessageGraph.Requirements("parent", messages).Executable);
        var audit = MessageContentPolicy.Apply("messages.12712", raw, "messages", messages);
        Assert.Equal("014A", audit!.DiscardedHex);
        var choice = Assert.IsType<MessageChoice>(Assert.Single(messages["messages.12712"].Nodes));
        Assert.Equal("A", MessageMarkup.Static(choice.Choices[0].OfType<MessageLiteral>()));
        Assert.Equal("B", MessageMarkup.Static(choice.Choices[1].OfType<MessageLiteral>()));
        Assert.True(MessageGraph.Requirements("parent", messages).Executable);
        Assert.False(source.FullyParsed);
        Assert.Null(MessageContentPolicy.Apply("messages.999", raw, "messages", messages));
        Assert.Null(MessageContentPolicy.Apply("base-messages.7762", raw, "base-messages", messages));
    }

    [Fact]
    public void PolicyDoesNotHideOtherBodyErrorsOrUnexpectedTails()
    {
        Assert.Throws<InvalidDataException>(() => MessageContentPolicy.Apply("messages.12630",
            [2, 1, 0xee, 5, 5, 5, 1, (byte)'J'], "messages", new Dictionary<string, MessageDefinition>()));
        Assert.Throws<InvalidDataException>(() => MessageContentPolicy.Apply("messages.12630",
            [(byte)'A', 5, 5, 5, 1, (byte)'C'], "messages", new Dictionary<string, MessageDefinition>()));
    }
}
