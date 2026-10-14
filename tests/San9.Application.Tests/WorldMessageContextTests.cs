using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class WorldMessageContextTests
{
    [Fact]
    public void OccurrenceChangesDateWhileKeepingTheCapturedWorld()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 17);
        var texts = Texts();
        texts.Messages.Add("date", new([new MessageValue(new("property", "day"))]));
        var context = session.Messages(texts);
        var next = session.Snapshot.Clock.Advance();
        var occurrence = context.AtOccurrence(next);
        Assert.Equal(next.Day, occurrence.Query("day", []).Number);
        Assert.Equal(session.Snapshot.Clock.Day, context.Query("day", []).Number);
        Assert.Equal(context.Query("person.name", [MessageBinding.Reference(MessageEntityKind.Person, 1)]),
            occurrence.Query("person.name", [MessageBinding.Reference(MessageEntityKind.Person, 1)]));
        Assert.Equal(next, occurrence.Produce("date", new Dictionary<string, MessageBinding>(), _ => 0).Clock);
    }

    [Fact]
    public void ProductionCapturesWorldAndRandomWithoutChangingSimulation()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 17);
        var before = session.Snapshot;
        var texts = Texts();
        texts.Messages.Add("greeting", new([
            new MessageValue(new("property", "person.name", Arguments: [new("binding", "speaker")])),
            new MessageRandomChoice([[new MessageLiteral("来了")], [new MessageLiteral("到了")]])]));
        var context = session.Messages(texts);
        int draws = 0;
        var message = context.Produce("greeting", new Dictionary<string, MessageBinding>
        { ["speaker"] = MessageBinding.Reference(MessageEntityKind.Person, 1) }, bound => { draws++; return bound - 1; });
        Assert.Equal("刘备到了", message.BbCode);
        Assert.Equal(session.Snapshot.Clock, message.Clock);
        StateAssert.Equal(before, session.Snapshot);
        Assert.Equal("刘备到了", message.BbCode);
        Assert.Equal(1, draws);
        Assert.Equal("刘备来了", context.Produce("greeting", new Dictionary<string, MessageBinding>
        { ["speaker"] = MessageBinding.Reference(MessageEntityKind.Person, 1) }, _ => 0).BbCode);
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
    public void StrategyConfirmationResolvesTheCurrentLeaderOfARuntimeFoundedForce()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        const int forceId = 9001;
        var force = new ForceState(forceId, 100)
        {
            LeaderId = 2,
            Origin = new(ForceCreationKind.CityIndependence, 1, 1, world.Clock)
        };
        world = world with { Forces = [.. world.Forces, force] };
        var texts = Texts();
        texts.Entries.Add("officer.2", "关羽");
        texts.Messages.Add("messages.6082", new([
            new MessageLiteral("将结束"),
            new MessageStyled("green-outline", [new MessageValue(new("property", "force.country-name",
                Arguments: [new("binding", "force.0")]))]),
            new MessageLiteral("军的战略面，\n您确定吗？")]));
        var values = new Dictionary<string, MessageBinding>
        {
            ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force, forceId)
        };
        var context = new WorldMessageContext(definition, world, texts);

        Assert.DoesNotContain(definition.Forces, f => f.Id == forceId);
        Assert.Equal("刘备", context.Query("force.name", [values["force.0"]]).Text);
        Assert.Equal("将结束[preset=green-outline]关羽[/preset]军的战略面，\n您确定吗？",
            context.Produce("messages.6082", values, _ => throw new InvalidOperationException()).BbCode);

        var afterSuccession = world with { Forces = [.. world.Forces.Where(f => f.Id != forceId), force with { LeaderId = 1 }] };
        Assert.Equal("将结束[preset=green-outline]刘备[/preset]军的战略面，\n您确定吗？",
            new WorldMessageContext(definition, afterSuccession, texts)
                .Produce("messages.6082", values, _ => throw new InvalidOperationException()).BbCode);
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
        ["officer.1"] = "刘备", ["surname"] = "刘", ["given"] = "备",
        ["region"] = "虎牢"
    }) { MessagePeople = new() { [1] = new("surname", "given", "portrait") } };
}
