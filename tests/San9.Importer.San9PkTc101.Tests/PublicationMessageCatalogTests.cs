using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PublicationMessageCatalogTests
{
    [Fact]
    public void CatalogPreservesCompleteArchiveIdentitiesInSourceOrder()
    {
        var expected = Enumerable.Range(0, 14209).Select(id => $"messages.{id}")
            .Concat(Enumerable.Range(0, 679).Select(id => $"biographies.{id}"));
        Assert.Equal(expected, PublicationMessageCatalog.Groups.Keys);
    }

    private static Dictionary<string, MessageDefinition> Archive() => PublicationMessageCatalog.Groups.Keys
        .ToDictionary(key => key, _ => new MessageDefinition([]));

    [Fact]
    public void PublicationPreservesIdentitiesOrderNestedReferencesAndDuplicateBodies()
    {
        var source = Archive();
        source["messages.7235"] = new([
            new MessageLiteral("返回"),
            new MessageStyled("yellow", [new MessageConditional(new("binding", "show"), [
                new MessageChoice(new("binding", "choice"), [
                    [new MessageReference("biographies.0", false)],
                    [new MessageRandomChoice([[new MessageReference("messages.8677", true)], [new MessageReturn()]])]
                ])
            ])]),
            new MessageValue(new("binding", "name"))]);
        source["biographies.0"] = new([new MessageLiteral("同文")]);
        source["messages.8677"] = new([new MessageLiteral("同文")]);
        var result = PublicationMessageCatalog.Compile(source.Reverse().ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(source.Count, result.Count);
        Assert.Equal(PublicationMessageCatalog.Groups.Keys, result.Keys);
        Assert.Equal(2, result.Values.Count(value => value.Nodes is [MessageLiteral { BbCode: "同文" }]));
        Assert.Equal(source.Count - 3, result.Values.Count(value => value.Nodes.Length == 0));

        foreach (var (key, original) in source)
            Assert.Equal(JsonSerializer.Serialize(original, PackageFiles.JsonOptions),
                JsonSerializer.Serialize(result[key], PackageFiles.JsonOptions));
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
