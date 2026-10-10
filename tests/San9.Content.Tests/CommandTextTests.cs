using San9.Content.Text;
using San9.Tests;

namespace San9.Content.Tests;

public sealed class CommandTextTests
{
    [Fact]
    public void AuthoredTextCanReorderArgumentsAndMissingTextNeverFallsBack()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, CommandTextFixture.Entries());
        texts.Entries[CommandText.Key("result.reward")] = "Gain {1}; officer {0}";
        CommandText.Validate(texts);
        Assert.Equal("Gain 12; officer Custom", CommandText.Resolve(texts, "result.reward", "Custom", 12));
        texts.Entries[CommandText.Key("result.reward")] = "{{{0}}}: {1}";
        CommandText.Validate(texts);
        Assert.Equal("{Custom}: 12", CommandText.Resolve(texts, "result.reward", "Custom", 12));
        texts.Entries[CommandText.Key("title.gift")] = "{{Gift}}";
        CommandText.Validate(texts);
        Assert.Equal("{Gift}", CommandText.Resolve(texts, "title.gift"));
        texts.Entries.Remove(CommandText.Key("title.gift"));
        Assert.Throws<InvalidDataException>(() => CommandText.Validate(texts));
        Assert.Throws<InvalidDataException>(() => CommandText.Resolve(texts, "title.gift"));
    }

    [Theory]
    [InlineData("Missing {1}")]
    [InlineData("Extra {0} {1} {2}")]
    [InlineData("Malformed {0} {1")]
    [InlineData("Escaped {{0}} {1}")]
    public void PackageRejectsIncompleteOrInvalidParameterBindings(string template)
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, CommandTextFixture.Entries());
        texts.Entries[CommandText.Key("result.reward")] = template;
        Assert.Throws<InvalidDataException>(() => CommandText.Validate(texts));
    }
}
