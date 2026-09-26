using San9.Content.Text;
using San9.Application.Presentation;

namespace San9.Application.Tests;

public sealed class MessageEvaluatorTests
{
    [Fact]
    public void PresetsRestoreOuterScopeAcrossChildAndSkippedBranch()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["m"] = Message(new MessageStyled("yellow", [Text("A"), new MessageReference("child", false), Text("C")]), Text("D")),
            ["child"] = Message(new MessageConditional(new("constant", Number: 0), [new MessageStyled("green", [Text("skipped")])]),
                new MessageStyled("blue-outline", [Text("B")]))
        });
        Assert.Equal("[preset=yellow]A[preset=blue-outline]B[/preset]C[/preset]D",
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
    public void EarlyReturnClosesPresetAndDoesNotLeakIntoCaller()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["m"] = Message(Text("A"), new MessageReference("child", false), Text("Z")),
            ["child"] = Message(new MessageStyled("green-outline", [Text("B"), new MessageReturn(), Text("unreachable")]), Text("C"))
        });
        Assert.Equal("A[preset=green-outline]B[/preset]Z", evaluator.Evaluate("m", Context()));
    }

    private static MessageLiteral Text(string text) => new(text);
    private static MessageDefinition Message(params MessageNode[] nodes) => new(nodes);
    private static MessageContext Context() => new(new Dictionary<string, MessageBinding>(),
        (_, _) => throw new InvalidDataException("No query registered."), _ => 0);

    [Fact]
    public void EntityIdentityIncludesKindAndRejectsNumericCoercion()
    {
        var person = new MessageExpression("entity", "Person", 9001);
        var force = new MessageExpression("entity", "Force", 9001);
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["same"] = Message(new MessageValue(new("=", Arguments: [person, person]))),
            ["different"] = Message(new MessageValue(new("=", Arguments: [person, force]))),
            ["numeric"] = Message(new MessageValue(new("=", Arguments: [person, new("constant", Number: 9001)]))),
            ["predicate"] = Message(new MessageConditional(person, [Text("bad")]))
        });
        Assert.Equal("1", evaluator.Evaluate("same", Context()));
        Assert.Equal("0", evaluator.Evaluate("different", Context()));
        Assert.Throws<InvalidDataException>(() => evaluator.Evaluate("numeric", Context()));
        Assert.Throws<InvalidDataException>(() => evaluator.Evaluate("predicate", Context()));
    }

    [Fact]
    public void UnsupportedQueryInDependencyIsRejected()
    {
        var evaluator = new MessageEvaluator(new Dictionary<string, MessageDefinition>
        {
            ["parent"] = Message(new MessageReference("child", false)),
            ["child"] = Message(new MessageValue(new("query", "unsupported")))
        });
        Assert.Throws<InvalidDataException>(() => evaluator.Evaluate("parent", Context()));
    }

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
        { ["m"] = Message(new MessageRandomChoice([[Text("A")],[Text("B")]])) });
        string result=evaluator.Evaluate("m",Context() with { RandomIndex = bound => { Assert.Equal(2,bound); calls++; return 1; } });
        Assert.Equal("B",result); Assert.Equal(1,calls);
    }

    [Fact]
    public void UnresolvedAndCyclicMessagesAreRejected()
    {
        var evaluator=new MessageEvaluator(new Dictionary<string,MessageDefinition>
        { ["m"] = Message(new MessageReference("m",true)) });
        Assert.Throws<InvalidDataException>(()=>evaluator.Evaluate("m",Context()));
        Assert.Throws<InvalidDataException>(()=>evaluator.Evaluate("unknown",Context()));
    }
}
