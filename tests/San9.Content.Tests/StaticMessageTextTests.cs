using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class StaticMessageTextTests
{
    [Fact]
    public void StaticTextCanReferenceTheCanonicalMessageBodyWhileDynamicTextRequiresEvaluation()
    {
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["label"] = "标题" })
        { Messages = new()
        {
            ["biographies.shared"] = new([new MessageLiteral("第一行\n第二行")]),
            ["dialogue"] = new([new MessageValue(new("binding", "person.0"))])
        } };
        Assert.Equal("标题", text.Resolve("label"));
        Assert.Equal("第一行\n第二行", text.ResolveRich("biographies.shared"));
        Assert.Throws<InvalidDataException>(() => text.ResolveRich("dialogue"));
        Assert.Throws<InvalidDataException>(() => text.ResolveRich("missing"));
        text.Validate();
    }
}
