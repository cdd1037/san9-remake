using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class MessageCatalogReferenceTests
{
    [Theory]
    [InlineData(MessageEntityKind.Rank)]
    [InlineData(MessageEntityKind.Treasure)]
    [InlineData(MessageEntityKind.Office)]
    [InlineData(MessageEntityKind.Formation)]
    [InlineData(MessageEntityKind.Tactic)]
    public void MissingCatalogEntitiesAreRejectedEvenInsideUnselectedBranches(MessageEntityKind kind)
    {
        Dictionary<string, MessageDefinition> messages = new()
        {
            ["nested"] = new([new MessageConditional(new("constant", Number: 0), [new MessageStyled("yellow", [
                new MessageChoice(new("constant", Number: 0), [[new MessageValue(new("entity", kind.ToString(), 9001))]])])])])
        };
        MessageGraph.Validate(messages);
        var error = Assert.Throws<InvalidDataException>(() => MessageGraph.ValidateCatalogReferences(messages, InformationDefinitions.Empty));
        Assert.Contains("nested", error.Message); Assert.Contains("9001", error.Message);
    }

    [Fact]
    public void ExistingSparseCatalogIdentitiesAndRuntimeBindingsRemainValid()
    {
        var catalog = InformationDefinitions.Empty with
        {
            Ranks = [new(9001, "爵位", 10000, 0, 0, 0)], Offices = [new(9002, "官职", 10000, 0)],
            Formations = [new(9003, "阵形")], Tactics = [new(9004, "兵法")], Treasures = [new(9005, "宝物", 11, 0, null)]
        };
        Dictionary<string, MessageDefinition> messages = new()
        {
            ["valid"] = new([new MessageValue(new("entity", "Rank", 9001)), new MessageValue(new("entity", "Office", 9002)),
                new MessageValue(new("entity", "Formation", 9003)), new MessageValue(new("entity", "Tactic", 9004)),
                new MessageValue(new("entity", "Treasure", 9005)),
                new MessageValue(new("binding", "speaker"))])
        };
        MessageGraph.Validate(messages);
        MessageGraph.ValidateCatalogReferences(messages, catalog);
    }
}
