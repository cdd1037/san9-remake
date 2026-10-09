using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class GameplayMessageCompilerTests
{
    [Theory]
    [InlineData(0x9E, "settlement.base-name-with-city")]
    [InlineData(0x9F, "settlement.name-with-city")]
    [InlineData(0xA7, "settlement.base-name")]
    [InlineData(0xA8, "settlement.name")]
    public void BuildingDisplaySelectorsPreserveTypeAndReceivingCitySemantics(byte selector, string expected)
    {
        var source = new OriginalMessageParser().Parse([2, 41, selector, 5, 5, 5]);
        Assert.True(source.SemanticsConfirmed);
        var messages = new GameplayMessageCompiler(Scenario(), [], 0, 0)
            .Compile(new Dictionary<string, MessageDefinition> { ["place"] = source.Definition });
        var expression = MessageGraph.Walk(messages["place"].Nodes).OfType<MessageValue>().Single().Expression;
        Assert.Equal(expected, expression.Binding);
        Assert.Equal("building.0", Assert.Single(expression.Arguments!).Binding);
    }

    [Fact]
    public void SourceBiographyBecomesIdentityAndWorldBitsBecomeEventKeys()
    {
        var scenario = Scenario() with { Officers = [new("person", new(0, null, null, new(50, 50, 50, 50), false))] };
        var compiler = new GameplayMessageCompiler(scenario, [Person(496)], 2, 0);
        var compiled = compiler.Compile(new Dictionary<string, MessageDefinition>
        {
            ["identity"] = Message(new("=", Arguments: [new("binding", "person.0.biography-resource"), new("constant", Number: 496)])),
            ["history"] = Message(new("query", "world-flag", Arguments: [new("constant", Number: 12)])),
            ["scenario"] = Message(new("query", "scenario"))
        });
        var identity = Expression(compiled["identity"]);
        Assert.Equal(new MessageExpression("entity", "Person", 0), identity.Arguments![1]);
        Assert.Equal(["person.0"], MessageGraph.Requirements("identity", compiled).Bindings);
        var history = Expression(compiled["history"]);
        Assert.Equal("event.occurred", history.Binding);
        Assert.Equal("qiao-marriage", Assert.Single(history.Arguments!).Binding);
        Assert.Equal(2, Expression(compiled["scenario"]).Number);
        Assert.All(GameplayMessageCompiler.Audit(compiled), row => Assert.True(row.RuntimeReady));
    }

    [Theory]
    [InlineData(37, "Personnel")]
    [InlineData(52, "Automatic")]
    public void ReturnMessagesRequireCapturedEventReasonInsteadOfInternalTaskCode(int code, string kind)
    {
        var compiler = new GameplayMessageCompiler(Scenario(), [], 0, 0);
        var messages = compiler.Compile(new Dictionary<string, MessageDefinition>
        { ["return"] = Message(new("=", Arguments: [new("binding", "unit.0.command-event-type"), new("constant", Number: code)])) });
        var expression = Expression(messages["return"]);
        Assert.Equal("return-kind.0", expression.Arguments![0].Binding);
        Assert.Equal(new MessageExpression("text", kind), expression.Arguments[1]);
        Assert.Equal(["return-kind.0"], MessageGraph.Requirements("return", messages).Bindings);
    }

    [Fact]
    public void UnknownRuntimePropertyStopsCompilationWithMessageIdentity()
    {
        var compiler = new GameplayMessageCompiler(Scenario(), [], 0, 0);
        var error = Assert.Throws<InvalidDataException>(() => compiler.Compile(new Dictionary<string, MessageDefinition>
        {
            ["parent"] = new([new MessageReference("child", false)]),
            ["child"] = Message(new("binding", "person.0.future-property"))
        }));
        Assert.Contains("child", error.Message);
        Assert.Contains("No runtime property for person.future-property.", error.Message);
    }

    private static MessageExpression Expression(MessageDefinition message) => Assert.IsType<MessageValue>(Assert.Single(message.Nodes)).Expression;
    private static MessageDefinition Message(MessageExpression expression) => new([new MessageValue(expression)]);
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "test", 190, 1, 1, 0, [], [], [], []);
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);
}
