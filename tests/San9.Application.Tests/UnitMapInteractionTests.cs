using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class UnitMapInteractionTests
{
    [Fact]
    public void UnitContextUsesCurrentUnitAndPreservesReadOnlyNavigation()
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        var unit = new UnitState(1, 1, new(12, 12), 18000)
            { CommanderId = 1, CorpsId = 1, ServicePlaceId = 2, FormationId = 100, Morale = 85 };
        definition = definition with { Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
            [new(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land)], []), Information = definition.Information with { Formations = [new(100, "formation")] } };
        var world = original with { Units = [unit], Officers = original.Officers.Select(o => o.Id is 1 or 5
            ? o with { UnitId = 1, SettlementId = null, Status = o.Id == 5 ? OfficerStatus.Captive : o.Status } : o).ToArray() };
        var keys = definition.Officers.Select(o => o.NameKey).Concat(definition.Forces.Select(f => f.NameKey))
            .Concat(definition.Settlements.Select(s => s.NameKey)).Append("formation");
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, keys.ToDictionary(k => k, k => k));
        var view = new StrategySnapshot(definition, world);
        var hash = world;
        var card = UnitMapInteraction.Hover(view, texts, 1)!;
        Assert.Equal("officer.1队", card.Name);
        Assert.Equal(["一般", "officer.1", "city.2", "18000", "85/100", "formation", "1"], card.Rows.Select(r => r.Value));
        var menu = UnitMapInteraction.Menu(view, 1);
        Assert.Equal(8, menu.Count);
        Assert.Equal(InformationCategory.Units, menu[0].Target.Category);
        Assert.Equal(1, menu[0].DetailId);
        Assert.Equal(1, menu[1].Target.UnitId);
        Assert.All(menu.Skip(2), e => Assert.Equal(1, e.Target.ForceId));
        Assert.Equal([1, 5], OfficerCatalog.Read(view, texts, 0, null, null, unitFilter: menu[1].Target.UnitId).Rows.Select(r => r.Key.Id).Order());
        var browser = new InformationBrowser();
        browser.OpenContext(menu[0]);
        Assert.True(browser.IsDetail); Assert.Equal(1, browser.SelectedId);
        browser.Back(); Assert.False(browser.IsOpen);
        var changed = new StrategySnapshot(definition, world with { Units = [unit with { Soldiers = 9000, Morale = 42, Condition = UnitCondition.Confused }] });
        Assert.Equal("混乱", UnitMapInteraction.Hover(changed, texts, 1)!.Rows[0].Value);
        Assert.Equal("9000", UnitMapInteraction.Hover(changed, texts, 1)!.Rows[3].Value);
        foreach (var hidden in new[] { unit with { IsEmbedded = true }, unit with { IsPersonnel = true }, unit with { Soldiers = 0 }, unit with { FormationId = null } })
        {
            var absent = new StrategySnapshot(definition, world with { Units = [hidden] });
            Assert.Null(UnitMapInteraction.Hover(absent, texts, 1));
            Assert.Empty(UnitMapInteraction.Menu(absent, 1));
        }
        Assert.Empty(UnitMapInteraction.Menu(new(definition, world with { Units = [], Officers = original.Officers }), 1));
        StateAssert.Equal(hash, world);
    }
}
