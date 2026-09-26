using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class WorldMessageContextTests
{
    [Fact]
    public void ProductionCapturesWorldAndRandomWithoutChangingSimulation()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 17);
        string before = session.Snapshot.StateHash;
        var texts = Texts();
        texts.Messages.Add("greeting", new([
            new MessageValue(new("property", "person.name", Arguments: [new("binding", "speaker")])),
            new MessageChoice(new("random", Arguments: [new("constant", Number: 2)]),
                [[new MessageLiteral("来了", "來了")], [new MessageLiteral("到了", "到了")]])], true, ["speaker"]));
        var context = session.Messages(texts);
        int draws = 0;
        var message = context.Produce("greeting", new Dictionary<string, MessageBinding>
        { ["speaker"] = MessageBinding.Reference(MessageEntityKind.Person, 1) }, bound => { draws++; return bound - 1; });
        Assert.Equal("刘备到了", message.BbCode);
        Assert.Equal(session.Snapshot.Clock, message.Clock);
        Assert.Equal(before, session.Snapshot.StateHash);
        Assert.Equal("刘备到了", message.BbCode);
        Assert.Equal(1, draws);
        Assert.Equal("劉備來了", context.Produce("greeting", new Dictionary<string, MessageBinding>
        { ["speaker"] = MessageBinding.Reference(MessageEntityKind.Person, 1) }, _ => 0, source: true).BbCode);
    }

    [Fact]
    public void PropertiesUseTypedReferencesAndSupportIdsOutsideSourceSlots()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var officer = definition.Officers[0];
        definition = definition with { Officers = [.. definition.Officers, officer with { State = officer.State with { Id = 9001 } }] };
        world = world with { Officers = [.. world.Officers, world.Officers[0] with { Id = 9001 }] };
        var query = new WorldMessageContext(definition, world, Texts());
        Assert.Equal("刘备", query.Query("person.name", [MessageBinding.Reference(MessageEntityKind.Person, 9001)]).Text);
        Assert.Throws<InvalidDataException>(() => query.Query("person.name", [MessageBinding.Reference(MessageEntityKind.Force, 1)]));
        Assert.Throws<InvalidDataException>(() => query.Query("person.name", [new(1)]));
        Assert.Throws<InvalidDataException>(() => query.Query("person.name", [MessageBinding.Reference(MessageEntityKind.Person, 9999)]));
        Assert.Throws<InvalidDataException>(() => query.Query("person.unknown", [MessageBinding.Reference(MessageEntityKind.Person, 1)]));
    }

    [Fact]
    public void RelationsNamesAndEventHistoryReadTheCapturedState()
    {
        var definition = G01ScenarioTests.Fixture() with { RegionNameKeys = new() { [1] = "region" } };
        var world = GameSession.Start(definition, 1).Snapshot;
        world = world with
        {
            Officers = world.Officers.Select(p => p.Id == 2 ? p with { ParentId = 1 } :
                p.Id == 1 ? (p with { Injury = 2 }).WithLoyalty(1, 150) : p).ToImmutableArray(),
            Simulation = world.Simulation with { ConsumedEventKeys = [new("qiao-marriage")] }
        };
        var query = new WorldMessageContext(definition, world, Texts());
        MessageBinding a = MessageBinding.Reference(MessageEntityKind.Person, 1), b = MessageBinding.Reference(MessageEntityKind.Person, 2);
        Assert.Equal(1, query.Query("parent-child", [a, b]).Number);
        Assert.Equal(1, query.Query("parent-child", [b, a]).Number);
        Assert.Equal(1, query.Query("senior-kin", [a, b]).Number);
        Assert.Equal(0, query.Query("senior-kin", [b, a]).Number);
        Assert.Equal("刘", query.Query("person.surname", [a]).Text);
        Assert.Equal("备", query.Query("person.given-name", [a]).Text);
        Assert.Equal(48, query.Query("person.war", [a]).Number);
        Assert.Equal(100, query.Query("person.loyalty", [a]).Number);
        Assert.Equal("虎牢", query.Query("settlement.base-name", [MessageBinding.Reference(MessageEntityKind.Settlement, 1)]).Text);
        Assert.Equal(1, query.Query("event.occurred", [new(0, "qiao-marriage")]).Number);
        Assert.Equal(0, query.Query("event.occurred", [new(0, "chained-plot.completed")]).Number);
        Assert.Equal(0, query.Query("force-relation", [MessageBinding.Reference(MessageEntityKind.Force, 1), MessageBinding.Reference(MessageEntityKind.Force, 2)]).Number);
    }

    private static TextCatalog Texts() => new(TextCatalog.CurrentSchemaVersion, new()
    {
        ["officer.1"] = new("刘备", "劉備"), ["surname"] = new("刘", "劉"), ["given"] = new("备", "備"),
        ["region"] = new("虎牢", "虎牢")
    }) { MessagePeople = new() { [1] = new("surname", "given", "portrait") } };
}
