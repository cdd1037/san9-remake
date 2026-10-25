using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class CapturePresentationCompilerTests
{
    [Fact]
    public void CaptureIncludesBothOriginalThirdPartyReceiptMessages()
    {
        var content = CapturePresentationCompiler.Compile();
        Assert.Equal("messages.7388", content.Messages["won-transfer"].Key);
        Assert.Equal("messages.7390", content.Messages["lost-transfer"].Key);
        foreach (string role in new[] { "won-transfer", "lost-transfer" })
        {
            Assert.Equal("force", content.Messages[role].Bindings["force.0"]);
            Assert.Equal("ruler", content.Messages[role].Bindings["person.0"]);
            Assert.Equal("place", content.Messages[role].Bindings["building.0"]);
        }
    }
}
