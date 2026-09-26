using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Application.Tests;

public sealed class MessageSimplificationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void BranchReturnsKeepOuterContinuationAndChosenQueries(int condition, int choice)
    {
        var original = new Dictionary<string, MessageDefinition>
        {
            ["message"] = new([
                Text("A"),
                new MessageConditional(new("binding", "condition"), [Text("B"), new MessageReturn(), Text("dead")]),
                new MessageRandomChoice([
                    [Text("C"), new MessageReturn()],
                    [new MessageReference("child", false), Text("D")]]),
                Text("E"), new MessageReturn(), Text("dead")]),
            ["child"] = new([Text("F"), new MessageReturn(), new MessageValue(new("query", "unreachable"))])
        };
        var simplified = Simplify(original);
        string expected = condition == 1 ? "AB" : choice == 0 ? "AC" : "AFDE";
        Assert.Equal(expected, Evaluate(original, condition, choice));
        Assert.Equal(expected, Evaluate(simplified, condition, choice));
        Assert.Equal(2, MessageGraph.Walk(simplified["message"].Nodes).OfType<MessageReturn>().Count());
        Assert.DoesNotContain(MessageGraph.Walk(simplified["child"].Nodes), node => node is MessageReturn or MessageValue);
        Assert.Empty(MessageGraph.Requirements("child", simplified).Bindings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExhaustiveReturningChoicesRemoveUnreachableSuffixAndKeepRandomDraw(int choice)
    {
        var original = new Dictionary<string, MessageDefinition>
        {
            ["message"] = new([new MessageRandomChoice([
                [Text("A"), new MessageReturn()], [Text("B"), new MessageReturn()]]),
                new MessageValue(new("binding", "unreachable"))])
        };
        var simplified = Simplify(original);
        Assert.Equal(Evaluate(original, 0, choice), Evaluate(simplified, 0, choice));
        Assert.DoesNotContain(MessageGraph.Walk(simplified["message"].Nodes), node => node is MessageReturn or MessageValue);
        Assert.Empty(MessageGraph.Requirements("message", simplified).Bindings);
    }

    [Theory]
    [InlineData(false, "ABC")]
    [InlineData(true, "AB")]
    public void CallAndTailTransferKeepDistinctContinuations(bool tail, string expected)
    {
        var original = new Dictionary<string, MessageDefinition>
        {
            ["message"] = new([Text("A"), new MessageReference("child", tail), Text("C"), new MessageReturn()]),
            ["child"] = new([Text("B"), new MessageReturn()])
        };
        Assert.Equal(expected, Evaluate(original, 0, 0));
        var simplified = Simplify(original);
        Assert.Equal(expected, Evaluate(simplified, 0, 0));
        Assert.DoesNotContain(MessageGraph.Walk(simplified.Values.SelectMany(m => m.Nodes)), node => node is MessageReturn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LastNestedBranchCanFallThroughToMessageEnd(int condition)
    {
        var original = new Dictionary<string, MessageDefinition>
        {
            ["message"] = new([new MessageConditional(new("binding", "condition"),
                [new MessageConditional(new("binding", "condition"), [Text("A"), new MessageReturn()]), new MessageReturn()]),
                new MessageReturn()])
        };
        var simplified = Simplify(original);
        Assert.Equal(Evaluate(original, condition, 0), Evaluate(simplified, condition, 0));
        Assert.DoesNotContain(MessageGraph.Walk(simplified["message"].Nodes), node => node is MessageReturn);
        Assert.Equal(["condition"], MessageGraph.Requirements("message", simplified).Bindings);
    }

    private static Dictionary<string, MessageDefinition> Simplify(Dictionary<string, MessageDefinition> messages) =>
        messages.ToDictionary(p => p.Key, p => new MessageDefinition(MessageGraph.Simplify(p.Value.Nodes)));
    private static MessageLiteral Text(string text) => new(text);
    private static string Evaluate(Dictionary<string, MessageDefinition> messages, int condition, int choice)
    {
        int draws = 0;
        string result = new MessageEvaluator(messages).Evaluate("message", new(
            new Dictionary<string, MessageBinding> { ["condition"] = new(condition) },
            (_, _) => throw new InvalidOperationException("Unreachable query was executed."),
            bound => { draws++; Assert.Equal(2, bound); return choice; }));
        Assert.InRange(draws, 0, 1);
        bool chooses = messages["message"].Nodes.Any(n => n is MessageRandomChoice);
        if (chooses) Assert.Equal(messages["message"].Nodes.Any(n => n is MessageConditional) && condition == 1 ? 0 : 1, draws);
        return result;
    }
}
