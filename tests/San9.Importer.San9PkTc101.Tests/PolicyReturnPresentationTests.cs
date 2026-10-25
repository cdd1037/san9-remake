using San9.Content.Interface;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PolicyReturnPresentationTests
{
    [Fact]
    public void UnavailableReturnUsesAuthoredMessagesWithActorContextAndNoMissingDestinationBinding()
    {
        Dictionary<string, MessageDefinition> messages = [];
        ExecutionPresentationCompiler.AddMessages(messages);
        var content = ExecutionPresentationCompiler.Compile();
        string role = ProgressPresentationDefinition.ReturnUnavailableRole;
        var record = content.Records[role];
        var speech = content.MapSpeeches[role];
        var portrait = content.Feedback[role];
        MessageGraph.Validate(messages);
        Assert.Equal(new[] { "person.0" }, MessageGraph.Requirements(record.Key, messages).Bindings);
        Assert.Equal("actor", record.Bindings["person.0"]);
        Assert.Contains("无法返回：没有可返回的据点。", string.Concat(MessageGraph.Walk(messages[record.Key].Nodes).OfType<MessageLiteral>().Select(n => n.BbCode)));
        Assert.Equal("actor", speech.Speaker);
        Assert.Equal("actor", portrait.Speaker);
        Assert.Equal(speech.Key, portrait.Key);
        Assert.Empty(MessageGraph.Requirements(speech.Key, messages).Bindings);
        Assert.Equal("无法返回：没有可返回的据点。", MessageMarkup.Static(messages[speech.Key].Nodes));
    }
}
