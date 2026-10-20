using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class ForceNameProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfficerAffiliationFollowsItsOwnRulerEvenWhenHeldByAnotherForce(bool captive)
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        var world = original with
        {
            Forces = original.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 5 } : f).ToImmutableArray(),
            Officers = original.Officers.Select(o => o.Id == 6 ? o with
            {
                Status = captive ? OfficerStatus.Captive : OfficerStatus.Serving,
                SettlementId = captive ? 1 : 2
            } : o).ToImmutableArray()
        };
        var view = new StrategySnapshot(definition, world);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new Dictionary<string, string>
            { ["officer.1"] = "玩家君主", ["officer.4"] = "刘表", ["officer.5"] = "刘琦", ["officer.6"] = "部下", ["city.1"] = "玩家城", ["city.2"] = "襄阳" });
        var row = Assert.Single(OfficerCatalog.Read(view, texts, 1, 2, null, officerId: 6).Rows);
        Assert.Equal("刘琦", row.Cells[1].Text);
        Assert.Equal("刘琦", OfficerDetails.Read(view, texts, 6).Fields[0][0].Value);
        Assert.Equal(2, view.Officers.Single(o => o.Id == 6).OwnerId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CurrentNamesFollowSuccessionAcrossInformationAndHoverWithoutChangingForceIdentity(bool created, bool barbarian)
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        original = original with { Forces = original.Forces.Select(f => f.Id == 2 ? f with
        {
            IsBarbarian = barbarian,
            Origin = created ? new(ForceCreationKind.EmptyCityUprising, 4, 2, original.Clock) : null
        } : f).ToImmutableArray() };
        var before = new StrategySnapshot(definition, original);
        var world = original with { Forces = original.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 5 } : f).ToImmutableArray() };
        var view = before.ForWorld(world);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new Dictionary<string, string>
        { ["force.2"] = "异民族", ["officer.4"] = "刘表", ["officer.5"] = "刘琦", ["city.2"] = "襄阳" });
        string expected = barbarian ? "异民族" : "刘琦";
        Assert.Equal(expected, MapHoverInfo.Resolve(view, texts, 2, MapInformationContext.Strategy)!.Rows.Single(r => r.Label == "势力").Value);
        var catalog = new AdministrativeCatalog(view, texts);
        Assert.Equal(expected, catalog.Cell(InformationCategory.Forces, 2, "name").Text);
        Assert.Equal(expected, catalog.Read(InformationCategory.Forces, 0, force: 2).Rows.Single().Cells[0].Text);
        Assert.Equal(expected, AdministrativeDetails.Read(view, texts, new(InformationCategory.Forces, 2)).Name);
        Assert.Equal("刘琦", AdministrativeDetails.Read(view, texts, new(InformationCategory.Forces, 2)).LeaderName);
        Assert.Equal(original.Forces.Select(f => f.Id), world.Forces.Select(f => f.Id));
        Assert.Equal(barbarian ? "force.2" : "officer.5", view.Forces.Single(f => f.Id == 2).NameKey);
        var identity = new PresentationIdentity(before, view);
        identity.Apply(new San9.Core.Simulation.ForceLeaderChangedEvent(world.Clock, 2, 4, 5));
        Assert.Equal(expected, new AdministrativeCatalog(identity.View, texts).Cell(InformationCategory.Forces, 2, "name").Text);
        if (!barbarian)
            Assert.Equal("刘琦", MapHoverInfo.Resolve(identity.View, texts, 2, MapInformationContext.Strategy)!.Rows.Single(r => r.Label == "势力").Value);
        Assert.Equal(barbarian ? "force.2" : "officer.4", before.Forces.Single(f => f.Id == 2).NameKey);
        Assert.Equal("force.2", definition.Forces.Single(f => f.Id == 2).NameKey);
    }
}
