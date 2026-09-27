using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class CommandPresentationTests
{
    [Fact]
    public void AuthoredBindingsAcceptCustomIdentitiesAndRejectDanglingReferences()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["custom.patrol"] = new([new MessageLiteral("Custom dialogue")]) } };
        var roles = new[] { "normal", "cooperative", "citizen", "pair-first", "pair-second", "result" }
            .ToDictionary(role => role, _ => "custom.patrol");
        var patrol = new PatrolPresentationDefinition(roles, 9001,
            [new("custom.patrol", 9001, [8001], AdviceMetric.PublicOrder, 500)]);
        var content = new CommandPresentation(new() { ["patrol-citizen"] = "audio/citizen.wav", ["domestic-result"] = "audio/result.wav" }, patrol);
        content.Validate(texts, [9001], [8001]);
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, [], [8001]));
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, [9001], []));
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = [] }).Validate(texts, [9001], [8001]));
        Assert.Throws<InvalidDataException>(() => content.Validate(new(TextCatalog.CurrentSchemaVersion, []), [9001], [8001]));
    }
}
