using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class EventPresentationCompilerTests
{
    [Fact]
    public void AbilityMessagesResolveSourceDiscriminatorsDuringImportRatherThanInTheApplication()
    {
        Dictionary<string, MessageDefinition> messages = new()
        {
            ["messages.9417"] = new([.. new[] { 1,0,2 }.Select(index=>(MessageNode)new MessageConditional(
                new("=",Arguments:[new("binding","number.0"),new("constant",Number:index)]),
                [new MessageLiteral(index.ToString()),new MessageValue(new("binding","number.1")),new MessageReturn()])),
                new MessageLiteral("3"),new MessageValue(new("binding","number.1"))])
        };
        EventPresentationCompiler.AddMessages(messages);
        string[] abilities = ["leadership", "war", "intelligence", "politics"];
        for (int index = 0; index < abilities.Length; index++)
        {
            string key = "presentation.events.ceremony-" + abilities[index];
            Assert.Equal(index.ToString(), Assert.IsType<MessageLiteral>(messages[key].Nodes[0]).BbCode);
            Assert.Equal(new[] { "number.1" }, MessageGraph.Requirements(key, messages).Bindings);
        }
    }
}
