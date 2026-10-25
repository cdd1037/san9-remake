using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class CapturePresentationContentTests
{
    [Fact]
    public void CaptureBindingsRequireCompleteMusicFacilityAndIllustrationReferences()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["result"] = new([new MessageLiteral("result")]) } };
        var content = new CapturePresentationDefinition(new()
            { ["won"] = new("result", []), ["lost"] = new("result", []),
                ["won-transfer"] = new("result", []), ["lost-transfer"] = new("result", []) },
            new() { [10000] = new("city", true) }, "victory", "loss", "capture");
        var sounds = new Dictionary<string, string> { ["victory"] = "victory.ogg", ["loss"] = "loss.ogg", ["capture"] = "capture.wav" };
        content.Validate(texts, sounds, []);
        content.ValidateReferences([10000], new Dictionary<string, string> { ["city"] = "asset" });
        foreach (string role in new[] { "won-transfer", "lost-transfer" })
        {
            var missing = content with { Messages = new(content.Messages) };
            missing.Messages.Remove(role);
            Assert.Throws<InvalidDataException>(() => missing.Validate(texts, sounds, []));
        }
        var invalidRole = content with { Messages = new(content.Messages)
            { ["won"] = new("result", new() { ["unused"] = "unknown" }) } };
        Assert.Throws<InvalidDataException>(() => invalidRole.Validate(texts, sounds, []));
        sounds.Remove("loss");
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, sounds, []));
        Assert.Throws<InvalidDataException>(() => content.ValidateReferences([10001], new Dictionary<string, string> { ["city"] = "asset" }));
        Assert.Throws<InvalidDataException>(() => content.ValidateReferences([10000], new Dictionary<string, string>()));
    }
}
