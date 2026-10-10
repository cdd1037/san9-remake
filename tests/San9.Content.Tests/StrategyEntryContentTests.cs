using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class StrategyEntryContentTests
{
    [Fact]
    public void ProgressRequiresOrderPromptBindingsAndBothSoundReferences()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = new()
        { ["prompt"] = new([new MessageValue(new("property","person.name",Arguments:[new("binding","person.0")]))]),
            ["literal"] = new([new MessageLiteral("text")]) } };
        var progress = new ProgressPresentationDefinition(
            ProgressPresentationDefinition.RecordRoles.ToDictionary(r=>r,_=>new DecisionMessageDefinition("literal",[])),
            ProgressPresentationDefinition.ContextRoles.ToDictionary(r=>r,_=>new DecisionMessageDefinition("literal",[])))
        { OrderPrompt = new("prompt",new() { ["person.0"]="ruler" },Sound:"prompt-sound"), StrategyTitleSound="title-sound" };
        var content = CommandPresentation.Empty with { Sounds = new() { ["prompt-sound"]="prompt.wav",["title-sound"]="title.wav" }, Progress=progress };
        content.Validate(texts,[1],[]);
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = new() { ["prompt-sound"]="prompt.wav" } }).Validate(texts,[1],[]));
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = new() { ["title-sound"]="title.wav" } }).Validate(texts,[1],[]));
        Assert.Throws<InvalidDataException>(() => (content with { Progress = progress with { OrderPrompt = progress.OrderPrompt with { Bindings=[] } } }).Validate(texts,[1],[]));
    }
}
