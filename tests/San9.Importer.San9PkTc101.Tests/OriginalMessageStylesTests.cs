using System.Text;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class OriginalMessageStylesTests
{
    private static MessageDefinition Parse(string text) =>
        new OriginalMessageParser().ParseSyntax(Encoding.ASCII.GetBytes(text), terminated: false).Definition;

    [Fact]
    public void ChildChangesAreResolvedIntoCallerContinuationAtImportTime()
    {
        var child = Parse("B\x1b[4bC");
        var parent = Parse("\x1b[1bA");
        parent = new([.. parent.Nodes, new MessageReference("child", false), new MessageLiteral("D")]);
        var result = OriginalMessageStyles.Compile(parent, new Dictionary<string, MessageDefinition> { ["child"] = child });
        Assert.Equal("[preset=blue-outline]A[/preset][preset=blue-outline]B[/preset][preset=green-outline]C[/preset][preset=green-outline]D[/preset]",
            MessageMarkup.Static(result.Nodes));
        Assert.DoesNotContain(MessageGraph.Walk(result.Nodes), n => n is OriginalStyleChange or MessageReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceBranchStyleAffectsOnlyItsExecutedContinuation(bool tail)
    {
        var child = new MessageDefinition([
            new MessageConditional(new("binding", "condition"), [new OriginalStyleChange("green-outline"),
                new MessageLiteral("B"), new MessageReturn()]),
            new OriginalStyleChange(null), new MessageLiteral("C")]);
        var parent = new MessageDefinition([new OriginalStyleChange("blue-outline"), new MessageLiteral("A"),
            new MessageReference("child", tail), new MessageLiteral("D")]);
        var result = OriginalMessageStyles.Compile(parent, new Dictionary<string, MessageDefinition> { ["child"] = child });
        var branch = Assert.IsType<MessageConditional>(result.Nodes[1]);
        Assert.Equal(tail ? "[preset=green-outline]B[/preset]" : "[preset=green-outline]B[/preset][preset=green-outline]D[/preset]",
            MessageMarkup.Static(branch.Body.TakeWhile(n => n is not MessageReturn)));
        Assert.Equal(tail ? "C" : "CD", MessageMarkup.Static(result.Nodes.Skip(2).TakeWhile(n => n is not MessageReturn)));
        Assert.IsType<MessageReturn>(branch.Body[^1]);
    }

    [Fact]
    public void StyleFreeReferencesStayReferencesAndInheritTheirScope()
    {
        var parent = new MessageDefinition([new OriginalStyleChange("blue-outline"), new MessageReference("child", false)]);
        var result = OriginalMessageStyles.Compile(parent,
            new Dictionary<string, MessageDefinition> { ["child"] = new([new MessageLiteral("B")]) });
        Assert.IsType<MessageReference>(Assert.Single(Assert.IsType<MessageStyled>(Assert.Single(result.Nodes)).Body));
    }
}
