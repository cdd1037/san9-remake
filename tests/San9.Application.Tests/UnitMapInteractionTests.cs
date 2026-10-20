using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class UnitMapInteractionTests
{
    [Fact]
    public void PortDefectionUpdatesConstructionArmyServicePlaceInTheWorldAndHover()
    {
        var definition = G01ScenarioTests.Fixture();
        definition = definition with { Settlements = [.. definition.Settlements,
            definition.Settlements[1] with { Id = 3, NameKey = "city.3" }],
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [new(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land)], []) };
        var original = GameSession.Start(definition with { Military = null }, 1).Snapshot;
        var order = new MilitaryOrder(MilitaryOrderKind.Construct, null, null)
            { Construction = new(4, 9000, new(12, 12), 4) };
        var state = WorldState.Restore(original with
        {
            Units = [new(77, 2, new(12, 12), 1000) { CommanderId = 4, ServicePlaceId = 2,
                FormationId = 100, Morale = 70, CorpsId = 2, Order = order,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(4, BattleRank.Front) }],
            Simulation = original.Simulation with { NextUnitId = 78 },
            Officers = original.Officers.Select(o => o.Id == 4 ? o with
                { SettlementId = null, UnitId = 77, IsCommander = false } : o).ToImmutableArray()
        });
        var world = state.BeginMutation();
        var rules = new SimulationRules { Personnel = new([new(1, new(2, 2)), new(2, new(7, 7)), new(3, new(17, 12))]) };
        List<DomainEvent> events = [];
        SettlementAffiliation.Transfer(world, world.Settlements[2], 1, world.Officers[5],
            [world.Officers[5], world.Officers[6]], SettlementDefectionKind.Isolated, rules, events);
        state.Commit(world);
        var view = new StrategySnapshot(definition, state.CreateSnapshot());
        var texts = MissionScenario.Texts(view);
        Assert.Equal(1, view.World.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.Equal(3, view.World.Units.Single().ServicePlaceId);
        Assert.Equal(2, view.World.Units.Single().OwnerId);
        Assert.Equal(order, view.World.Units.Single().Order);
        var hover = UnitMapInteraction.Hover(view, texts, 77)!;
        Assert.Equal(texts.Resolve("city.3"), hover.Rows.Single(r => r.Label == "所属").Value);
        Assert.Equal(texts.Resolve("officer.4"), hover.Rows.Single(r => r.Label == "势力").Value);
        Assert.Equal(hover.Rows.Single(r => r.Label == "所属").Value,
            Assert.Single(UnitCatalog.Read(view, view.MilitaryRules!, texts).Rows).Cells[3].Text);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(null, false)]
    [InlineData(2, true)]
    public void ConstructionArmyHoverDoesNotKeepALostPortAsItsServicePlace(int? portOwner, bool destroyed)
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        definition = definition with { Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(),
            [new(0, 1, 1)], [new(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land)], []) };
        var unit = new UnitState(77, 2, new(12, 12), 1000) { CommanderId = 4, ServicePlaceId = 2,
            FormationId = 100, Morale = 70, CorpsId = 2,
            Order = new(MilitaryOrderKind.Construct, null, null) { Construction = new(3, 9000, new(12, 12), 4) } };
        var world = original with { Units = [unit] };
        var keys = definition.Officers.Select(o => o.NameKey).Concat(definition.Forces.Select(f => f.NameKey))
            .Concat(definition.Settlements.Select(s => s.NameKey));
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, keys.ToDictionary(k => k, k => k));
        var before = new StrategySnapshot(definition, world);
        Assert.Equal("city.2", UnitMapInteraction.Hover(before, texts, 77)!.Rows.Single(r => r.Label == "所属").Value);
        var changed = before.ForWorld(world with { Settlements = world.Settlements.Select(s => s.Id == 2
            ? s with { OwnerId = portOwner, IsDestroyed = destroyed } : s).ToImmutableArray() });
        var hover = UnitMapInteraction.Hover(changed, texts, 77)!;
        Assert.Equal("无", hover.Rows.Single(r => r.Label == "所属").Value);
        Assert.Equal(2, hover.Force!.Id);
        Assert.Equal("officer.4", hover.Rows.Single(r => r.Label == "势力").Value);
        Assert.Equal("无", Assert.Single(UnitCatalog.Read(changed, changed.MilitaryRules!, texts).Rows).Cells[3].Text);
        Assert.Same(unit, Assert.Single(changed.World.Units));
    }

    [Fact]
    public void UnitContextUsesCurrentUnitAndPreservesReadOnlyNavigation()
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        var unit = new UnitState(1, 1, new(12, 12), 18000)
            { CommanderId = 1, CorpsId = 1, ServicePlaceId = 1, FormationId = 100, Morale = 85 };
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
        Assert.Equal(["一般", "officer.1", "city.1", "18000", "85/100", "formation", "1"], card.Rows.Select(r => r.Value));
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
