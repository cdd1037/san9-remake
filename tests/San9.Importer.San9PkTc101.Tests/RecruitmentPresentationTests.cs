using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class RecruitmentPresentationTests
{
    [Fact]
    public void RecruitmentLossUsesTheOriginalNotificationAndKeepsTheJournalIndependent()
    {
        var content = ExecutionPresentationCompiler.Compile();
        var prompt = content.Feedback["recruit-lost"];
        Assert.Equal("messages.7165", prompt.Key);
        Assert.Equal("decision-decrease", prompt.Sound);
        Assert.Null(prompt.Speaker);
        Assert.Equal("actor", prompt.Bindings["person.0"]);
        Assert.Equal("ruler", prompt.Bindings["person.1"]);
        Assert.Equal("force", prompt.Bindings["force.0"]);
        Assert.Equal("messages.9921", content.Records["recruit-lost"].Key);
        Assert.Null(content.Records["recruit-lost"].Sound);
        Assert.Equal("messages.10047", content.Records["recruit-captive-other"].Key);
    }
}
