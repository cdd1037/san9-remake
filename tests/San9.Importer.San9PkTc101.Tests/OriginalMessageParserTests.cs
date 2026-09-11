using System.Text;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OriginalMessageParserTests
{
    [Fact]
    public void OutlineDoesNotOverwriteForegroundAndResetRestoresBase()
    {
        var parsed = new OriginalMessageParser().Parse([.. Encoding.ASCII.GetBytes("\x1b[2mA"), 2, 0x8C,
            .. Encoding.ASCII.GetBytes("1bB"), 2, 0x8C, .. Encoding.ASCII.GetBytes("32bC")], terminated: false);
        Assert.True(parsed.FullyParsed);
        Assert.True(parsed.Definition.SemanticsConfirmed);
        string markup = MessageMarkup.Static(parsed.Definition.Nodes);
        Assert.Contains("[outline_color=#98B0F8A6][color=#F06850]B", markup);
        Assert.DoesNotContain("[color=#98B0F8]", markup);
        Assert.EndsWith("[outline_size=0]C[/outline_size]", markup);
    }

    [Fact]
    public void InlineShadowIsConsumedButExplicitlyNotExecutable()
    {
        var bytes = Encoding.ASCII.GetBytes("\x1b[1sA");
        var parsed = new OriginalMessageParser().Parse(bytes, terminated: false);
        Assert.Equal(bytes.Length, parsed.ConsumedBytes);
        Assert.True(parsed.FullyParsed);
        Assert.False(parsed.Definition.SemanticsConfirmed);
        Assert.Contains(parsed.Issues, issue => issue.Offset == 0 && issue.Reason.Contains("shadow"));
    }

    [Fact]
    public void MissingRandomCandidateAndDeepExpressionAreIncomplete()
    {
        var parser = new OriginalMessageParser();
        Assert.False(parser.Parse([1, (byte)'S', 2, 0xCA, (byte)'%', (byte)'2', (byte)'A', 5, 5, 5]).FullyParsed);
        byte[] nested = [.. Enumerable.Range(0, 100).SelectMany(_ => new byte[] { 2, 0xCA }), (byte)'1', 5, 5, 5];
        Assert.False(parser.Parse(nested).FullyParsed);
        Assert.False(parser.Parse([(byte)'A']).FullyParsed);
    }

    [Fact]
    public void CityIconAndLiteralBracketsArePreserved()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] name = Encoding.GetEncoding(950).GetBytes("洛陽[b]");
        var result = new OriginalMessageParser().Parse([0xFA, 0xD0, .. name, 5, 5, 5]);
        Assert.True(result.FullyParsed);
        Assert.True(result.Definition.SemanticsConfirmed);
        Assert.Equal("[img]icons/city[/img]洛阳[lb]b]", string.Concat(result.Definition.Nodes.OfType<MessageLiteral>().Select(n => n.BbCode)));
        Assert.Throws<InvalidDataException>(() => MessageMarkup.Plain("[img]icons/city[/img]洛阳"));
    }

    [Fact]
    public void BranchDelimitersAreNotArchiveTerminators()
    {
        var result = new OriginalMessageParser().Parse([1, (byte)'S', 2, 0xCA, (byte)'%', (byte)'2', (byte)'A', 5,5,5, (byte)'B', 5,5,5]);
        var choice = Assert.IsType<MessageChoice>(Assert.Single(result.Definition.Nodes));
        Assert.Equal(2, choice.Choices.Length);
        Assert.True(result.Definition.SemanticsConfirmed);
    }

    [Fact]
    public void TruncatedOperandsDoNotCountAsFullyParsed()
    {
        var result = new OriginalMessageParser().Parse([(byte)'A', 5,5,5, 1,(byte)'J']);
        Assert.False(result.FullyParsed);
        Assert.False(result.Definition.SemanticsConfirmed);
        Assert.NotEmpty(result.Definition.Nodes);
    }

    [Fact]
    public void TailTransferAndCallAreDistinctAndConsumeBinaryArguments()
    {
        var result = new OriginalMessageParser().Parse([1,(byte)'J',5,5, 1,(byte)'C',7,0, 5,5,5]);
        var refs = result.Definition.Nodes.OfType<MessageReference>().ToArray();
        Assert.Equal("messages.1285", refs[0].Key); Assert.True(refs[0].Tail);
        Assert.Equal("messages.7", refs[1].Key); Assert.False(refs[1].Tail);
        Assert.True(result.FullyParsed);
    }

    [Fact]
    public void ObjectsInPredicatesDoNotConsumeFollowingControl()
    {
        var result = new OriginalMessageParser().Parse([5,5,4, 2,0xCE, 2,1, 2,2, (byte)'A',5,5,5,(byte)'B',5,5,5]);
        var condition = Assert.IsType<MessageConditional>(result.Definition.Nodes[0]);
        Assert.Equal("same-bloodline", condition.Predicate.Binding);
        Assert.Equal(2, condition.Predicate.Arguments!.Length);
        Assert.True(result.FullyParsed); Assert.True(result.Definition.SemanticsConfirmed);
    }

    [Fact]
    public void UnknownPropertiesAreConsumedButNeverExecutable()
    {
        var result = new OriginalMessageParser().Parse([2,1,0xEE, (byte)'A',5,5,5]);
        Assert.True(result.FullyParsed); Assert.False(result.Definition.SemanticsConfirmed);
        Assert.Contains(result.Issues, i => i.Reason.Contains("property EE"));
    }
}
