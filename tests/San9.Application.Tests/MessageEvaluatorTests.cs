using San9.Content.Text;
using San9.Application.Presentation;

namespace San9.Application.Tests;

public sealed class MessageEvaluatorTests
{
    [Fact]
    public void IndependentChannelsRemainBalancedAcrossChildAndSkippedBranch()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["m"] = Message(new MessageFormat("[color=red]", "[/color]"), Text("A"),
                new MessageReference("child", false), Text("C"), new MessageFormat("", "", "effect"), Text("D")),
            ["child"] = Message(new MessageFormat("[outline_size=1]", "[/outline_size]", "effect"),
                new MessageConditional(new("constant", Number: 0), [new MessageFormat("", "")]), Text("B"))
        });
        Assert.Equal("[color=red]A[/color][outline_size=1][color=red]BC[/color][/outline_size][color=red]D[/color]",
            evaluator.Evaluate("m", Context()));
    }

    [Theory]
    [InlineData(-12, 5, true, null, "-0012")]
    [InlineData(-12, 6, true, 4, " -0012")]
    [InlineData(0, 0, false, 0, "")]
    public void NumericFormatsRespectSignPrecisionAndPadding(long value, int width, bool zero, int? precision, string expected)
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        { ["m"] = Message(new MessageValue(new("constant", Number: value), Format: new("signed", width, zero, Precision: precision))) });
        Assert.Equal(expected, evaluator.Evaluate("m", Context()));
    }

    [Fact]
    public void StylesFollowChosenControlFlowAcrossCalls()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["m"] = Message(new MessageFormat("[color=red]", "[/color]"), Text("A"),
                new MessageConditional(new("constant", Number: 0), [new MessageFormat("[color=blue]", "[/color]")]),
                new MessageReference("child", false), Text("Z")),
            ["child"] = Message(Text("B"), new MessageFormat("", ""), Text("C"))
        });
        Assert.Equal("[color=red]AB[/color]CZ", evaluator.Evaluate("m", Context()));
    }

    private static MessageLiteral Text(string text) => new(text, text);
    private static MessageDefinition Message(params MessageNode[] nodes) => new(nodes, true, []);
    private static MessageContext Context() => new(new Dictionary<string, MessageBinding>(),
        (_, _) => throw new InvalidDataException("No query registered."), _ => 0);

    [Fact]
    public void TailTransferDoesNotAppendCallerSuffixButCallDoes()
    {
        var messages = new Dictionary<string, MessageDefinition>
        {
            ["tail"] = Message(Text("A"),new MessageReference("child",true),Text("Z")),
            ["call"] = Message(Text("A"),new MessageReference("child",false),Text("Z")),
            ["child"] = Message(Text("B"),new MessageReturn(),Text("unreachable"))
        };
        var evaluator = new MessageEvaluator(messages);
        Assert.Equal("AB",evaluator.Evaluate("tail",Context()));
        Assert.Equal("ABZ",evaluator.Evaluate("call",Context()));
    }

    [Fact]
    public void OrdinaryParametersCannotInjectMarkup()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        { ["m"] = Message(new MessageValue(new("binding","name"))) });
        var context = Context() with { Values = new Dictionary<string,MessageBinding> { ["name"] = new(0,"[img]bad[/img]") } };
        Assert.Equal("[lb]img]bad[lb]/img]",evaluator.Evaluate("m",context));
        Assert.Throws<InvalidDataException>(()=>evaluator.Evaluate("m",Context()));
    }

    [Fact]
    public void RandomIsChosenOnlyWhenProducingMessage()
    {
        int calls=0;
        var evaluator=new MessageEvaluator(new Dictionary<string,MessageDefinition>
        { ["m"] = Message(new MessageChoice(new("random",Arguments:[new("constant",Number:2)]), [[Text("A")],[Text("B")]])) });
        string result=evaluator.Evaluate("m",Context() with { RandomIndex = bound => { Assert.Equal(2,bound); calls++; return 1; } });
        Assert.Equal("B",result); Assert.Equal(1,calls);
    }

    [Fact]
    public void UnresolvedAndCyclicMessagesAreRejected()
    {
        var evaluator=new MessageEvaluator(new Dictionary<string,MessageDefinition>
        { ["m"] = Message(new MessageReference("m",true)), ["unknown"]=new([],false,[]) });
        Assert.Throws<InvalidDataException>(()=>evaluator.Evaluate("m",Context()));
        Assert.Throws<InvalidDataException>(()=>evaluator.Evaluate("unknown",Context()));
    }
}
