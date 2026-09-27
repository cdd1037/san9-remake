using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PublicationMessageCatalogTests
{
    [Fact]
    public void SourceIdMapKeepsArchiveIdentityAndZeroBasedIndicesWithFullCoverage()
    {
        var map = PublicationMessageCatalog.SourceIdMap();
        Assert.Equal(Enumerable.Range(0, 14209), map["M_MsgPK.s9"].Keys);
        Assert.Equal(Enumerable.Range(0, 679), map["M_RtdnPK.s9"].Keys);
        Assert.Equal("reports.unit-return", map["M_MsgPK.s9"][7235]);
        Assert.Equal("help.appearance.description", map["M_MsgPK.s9"][8677]);
        Assert.NotEqual(map["M_MsgPK.s9"][0], map["M_RtdnPK.s9"][0]);
        Assert.Equal(PublicationMessageCatalog.Keys.Values.Order(StringComparer.Ordinal),
            map.Values.SelectMany(archive => archive.Values).Order(StringComparer.Ordinal));
    }

    private static Dictionary<string, MessageDefinition> Archive() => PublicationMessageCatalog.Keys.Keys
        .ToDictionary(key => key, _ => new MessageDefinition([]));

    [Fact]
    public void RenamingIsLosslessIncludingNestedCrossCategoryReferencesAndDuplicateBodies()
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
        var result = PublicationMessageCatalog.Compile(source);
        Assert.Equal(source.Count, result.Count);
        Assert.Equal("reports.unit-return", PublicationMessageCatalog.Keys["messages.7235"]);
        Assert.Equal("help.appearance.description", PublicationMessageCatalog.Keys["messages.8677"]);
        Assert.Equal(2, result.Values.Count(value => value.Nodes is [MessageLiteral { BbCode: "同文" }]));
        Assert.Equal(source.Count - 3, result.Values.Count(value => value.Nodes.Length == 0));

        var reverse = PublicationMessageCatalog.Keys.ToDictionary(pair => pair.Value, pair => pair.Key);
        MessageNode[] Restore(MessageNode[] nodes) => nodes.Select<MessageNode, MessageNode>(node => node switch
        {
            MessageReference r => r with { Key = reverse[r.Key] },
            MessageConditional c => c with { Body = Restore(c.Body) },
            MessageChoice c => c with { Choices = c.Choices.Select(Restore).ToArray() },
            MessageRandomChoice c => c with { Choices = c.Choices.Select(Restore).ToArray() },
            MessageStyled s => s with { Body = Restore(s.Body) },
            _ => node
        }).ToArray();
        foreach (var (key, original) in source)
            Assert.Equal(JsonSerializer.Serialize(original, PackageFiles.JsonOptions),
                JsonSerializer.Serialize(new MessageDefinition(Restore(result[PublicationMessageCatalog.Keys[key]].Nodes)), PackageFiles.JsonOptions));
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
