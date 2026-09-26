using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class MessageStyleTests
{
    [Fact]
    public void StaticPresetRunsBecomeOneLiteralButDynamicScopesRemainStructured()
    {
        var nodes = MessageGraph.Simplify([new MessageLiteral("好！\n这下子，就能阻止"),
            new MessageStyled("blue-outline", [new MessageLiteral("刘备")]), new MessageLiteral("的援军了！")]);
        Assert.Equal("好！\n这下子，就能阻止[preset=blue-outline]刘备[/preset]的援军了！",
            Assert.IsType<MessageLiteral>(Assert.Single(nodes)).BbCode);
        var dynamic = MessageGraph.Simplify([new MessageStyled("blue-outline", [new MessageValue(new("binding", "name"))])]);
        Assert.IsType<MessageStyled>(Assert.Single(dynamic));
        MessageGraph.Validate(new Dictionary<string, MessageDefinition> { ["m"] = new(nodes) });
    }

    [Fact]
    public void ScopedPresetRoundTripsAndParticipatesInDependenciesAndBindings()
    {
        var message = new MessageDefinition([new MessageStyled("yellow", [new MessageReference("child", false),
            new MessageValue(new("binding", "count"))])]);
        string json = JsonSerializer.Serialize(message, PackageFiles.JsonOptions);
        var decoded = JsonSerializer.Deserialize<MessageDefinition>(json, PackageFiles.JsonOptions)!;
        var messages = new Dictionary<string, MessageDefinition> { ["m"] = decoded, ["child"] = new([new MessageLiteral("A")]) };
        MessageGraph.Validate(messages);
        Assert.Equal(["count"], MessageGraph.Requirements("m", messages).Bindings);
        messages.Remove("child");
        Assert.Throws<InvalidDataException>(() => MessageGraph.Validate(messages));
        Assert.DoesNotContain("channel", json);
        Assert.DoesNotContain("open", json);
    }

    [Fact]
    public void UnknownPresetsAndObsoleteStateInstructionsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => MessageGraph.Validate(new Dictionary<string, MessageDefinition>
            { ["m"] = new([new MessageStyled("palette-31", [new MessageLiteral("A")])]) }));
        Assert.Throws<InvalidDataException>(() => MessageGraph.Validate(new Dictionary<string, MessageDefinition>
            { ["m"] = new([new MessageLiteral("[preset=palette-31]A[/preset]")]) }));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageDefinition>(
            """[{"kind":"format","open":"[color=red]","close":"[/color]"}]""", PackageFiles.JsonOptions));
    }
}
