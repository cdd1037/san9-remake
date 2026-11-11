using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PublicationMessageCatalogTests
{
    [Fact]
    public void ReviewedBindingsLeaveUnconfirmedColumnsInTheLargeFile()
    {
        var groups = PublicationMessageCatalog.SourceGroups;
        Assert.Equal("scenario-catalog", groups["messages.0"]);
        Assert.Equal("scenario-catalog", groups["messages.39"]);
        Assert.Equal("person-names", groups["messages.1678"]);
        Assert.Equal("person-names", groups["messages.1680"]);
        Assert.Equal("tactics", groups["messages.1219"]);
        Assert.Equal("formations", groups["messages.5365"]);
        Assert.Equal("offices", groups["messages.7770"]);
        Assert.Equal("ranks", groups["messages.7761"]);
        Assert.Equal("geography", groups["messages.136"]);
        Assert.Equal("country-titles", groups["messages.5315"]);
        foreach (int id in new[] { 40, 1681, 1220, 5364 }) Assert.Equal("messages", groups[$"messages.{id}"]);
        Assert.Equal("names", groups["messages.11455"]);
    }

    [Fact]
    public void CatalogPreservesCompleteArchiveIdentitiesInSourceOrder()
    {
        var expected = Enumerable.Range(0, 14209).Select(id => $"messages.{id}")
            .Concat(Enumerable.Range(0, 679).Select(id => $"biographies.{id}"));
        Assert.Equal(expected, PublicationMessageCatalog.SourceGroups.Keys);
    }

    private static Dictionary<string, MessageDefinition> Archive() => PublicationMessageCatalog.SourceGroups.Keys
        .ToDictionary(key => key, _ => new MessageDefinition([]));

    [Fact]
    public void PublicationPreservesIdentitiesOrderNestedReferencesAndDuplicateBodies()
    {
        var source = Archive();
        source["messages.7235"] = new([
            new MessageLiteral("返回"),
            new MessageStyled("yellow", [new MessageConditional(new("binding", "show"), [
                new MessageChoice(new("binding", "choice"), [
                    [new MessageReference("messages.8676", false)],
                    [new MessageRandomChoice([[new MessageReference("messages.8677", true)], [new MessageReturn()]])]
                ])
            ])]),
            new MessageValue(new("binding", "name"))]);
        source["messages.8676"] = new([new MessageLiteral("同文")]);
        source["messages.8677"] = new([new MessageLiteral("同文")]);
        var result = PublicationMessageCatalog.Compile(source.Reverse().ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(source.Count - 4554, result.Count);
        Assert.Equal(PublicationMessageCatalog.Groups.Keys, result.Keys);
        Assert.Equal(2, result.Values.Count(value => value.Nodes is [MessageLiteral { BbCode: "同文" }]));
        Assert.Equal(result.Count - 3, result.Values.Count(value => value.Nodes.Length == 0));

        foreach (var (key, published) in result)
            Assert.Equal(JsonSerializer.Serialize(source[key], PackageFiles.JsonOptions),
                JsonSerializer.Serialize(published, PackageFiles.JsonOptions));
    }

    [Theory]
    [InlineData("messages.0")]
    [InlineData("messages.1678")]
    [InlineData("messages.11455")]
    [InlineData("messages.1219")]
    [InlineData("messages.5363")]
    [InlineData("messages.5065")]
    [InlineData("messages.1652")]
    [InlineData("messages.136")]
    [InlineData("messages.5315")]
    [InlineData("messages.1392")]
    [InlineData("messages.7641")]
    [InlineData("biographies.0")]
    public void ExtractedDataIsOmittedButRetainedMessagesCannotReferenceIt(string key)
    {
        var source = Archive();
        source[key] = new([new MessageLiteral("资料正文")]);
        Assert.DoesNotContain(key, PublicationMessageCatalog.Compile(source).Keys);

        source["messages.7235"] = new([new MessageConditional(new("constant", Number: 0),
            [new MessageChoice(new("constant", Number: 0), [[new MessageReference(key, false)]])])]);
        Assert.Throws<InvalidDataException>(() => PublicationMessageCatalog.Compile(source));
    }

    [Fact]
    public void UnreviewedArchiveChangesAndMissingReferencesFailPublication()
    {
        var source = Archive();
        source.Remove("messages.0");
        Assert.Throws<InvalidDataException>(() => PublicationMessageCatalog.Compile(source));
        source.Add("messages.14209", new([]));
        Assert.Throws<InvalidDataException>(() => PublicationMessageCatalog.Compile(source));
        source.Remove("messages.14209");
        source.Add("messages.0", new([new MessageReference("unmapped", true)]));
        Assert.Throws<InvalidDataException>(() => PublicationMessageCatalog.Compile(source));
    }
}
