using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class UnitCatalogTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TransportDestinationSurvivesTemporaryContactInEveryInformationView(bool settlementContact)
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        var unit = new UnitState(77, 1, new(12, 12), 1000) { CommanderId = 1, ServicePlaceId = 1,
            Order = new(MilitaryOrderKind.Transport, null, 2), ContactTarget = new(settlementContact ? 1 : 78, settlementContact) };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, definition.Officers.Select(o => o.NameKey)
            .Concat(definition.Forces.Select(f => f.NameKey)).Concat(definition.Settlements.Select(s => s.NameKey))
            .ToDictionary(key => key, key => key));
        foreach (int destination in new[] { 2, 1 })
        {
            unit = unit with { Order = unit.Order! with { SettlementId = destination } };
            var world = original with { Units = [unit, new(78, 2, new(13, 12), 1000) { CommanderId = 4 }] };
            var view = new StrategySnapshot(definition, world);
            var row = UnitCatalog.Read(view, null, texts, 4).Rows.Single(r => r.Key.Id == unit.Id);
            var detail = UnitDetails.Read(view, null, texts, unit.Id);
            var summary = MapCommandSummary.Unit(view, texts, unit.Id);
            Assert.Equal($"city.{destination}", row.Cells[2].Text);
            Assert.Equal(row.Cells[2].Text, detail.Orders[1].Value);
            Assert.Equal(detail.Orders.Select(f => f.Value), summary.Rows.Skip(5).Select(r => r.Value));
            Assert.True(unit.EffectiveOrder!.IsAttack);
        }
    }

    [Fact]
    public void PagesProjectLiveMembersFormationsOrdersAndHideEnemyCommands()
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 1).Snapshot;
        var unit = new UnitState(77, 1, new(12, 12), 32000) { CommanderId = 1, CorpsId = 1,
            ServicePlaceId = 1, FormationId = 100, NavalFormationId = 200, Morale = 50, Wounded = 123,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2), CommandedThisXun = true };
        var world = original with {
            Units = [unit, unit with { Id = 78, IsEmbedded = true }, unit with { Id = 79, IsPersonnel = true }],
            Officers = original.Officers.Select(o => o.Id is 1 or 5 ? o with { UnitId = 77, SettlementId = null,
                Status = o.Id == 5 ? OfficerStatus.Captive : o.Status } : o).ToArray()
        };
        definition = definition with { Information = definition.Information with {
            Formations = [new(100, "land"), new(200, "water")] } };
        var view = new StrategySnapshot(definition, world);
        var rules = new MilitaryRulebook(new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
            [new(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land) { Combat = new(11, 15, 10, 11, 50) },
             new(200, 60, 0, 100000, 5, new(1, 1), TravelMode.Water) { Combat = new(10, 10, 10, 10, 50) }], []));
        var keys = definition.Officers.Select(o => o.NameKey).Concat(definition.Forces.Select(f => f.NameKey))
            .Concat(definition.Settlements.Select(s => s.NameKey)).Concat(["land", "water"]);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, keys.ToDictionary(k => k, k => k));
        var hash = world;
        InformationTable Read(int page) => UnitCatalog.Read(view, rules, texts, page);
        Assert.Equal(["所属", "战力", "陆上", "水上", "命令"], Read(0).Pages);
        Assert.Equal(["officer.1队", "officer.1", "officer.1", "city.1", "1", "1"], Assert.Single(Read(0).Rows).Cells.Select(c => c.Text));
        Assert.Equal(["officer.1队", "32000", "123", "50", "一般", "land", "water"], Assert.Single(Read(1).Rows).Cells.Select(c => c.Text));
        // Leadership 90, morale 50 and 32,000 troops give base power 179 after integer truncation.
        Assert.Equal(["officer.1队", "157", "214", "6", "262", "10"], Assert.Single(Read(2).Rows).Cells.Select(c => c.Text));
        Assert.Equal(["officer.1队", "143", "143", "6", "238", "12"], Assert.Single(Read(3).Rows).Cells.Select(c => c.Text));
        Assert.Equal(["officer.1队", "攻击", "city.2", "无视", "不许可", "不许可", "撤退", "完"], Assert.Single(Read(4).Rows).Cells.Select(c => c.Text));
        Assert.Empty(UnitCatalog.Read(view, rules, texts, force: 2).Rows);
        var changed = world with { Units = [unit with { NavalFormationId = null, CommandedThisXun = false }] };
        Assert.All(Assert.Single(UnitCatalog.Read(new(definition, changed), rules, texts, 3).Rows).Cells.Skip(1), c => Assert.True(c.IsUnavailable));
        Assert.Equal("未", Assert.Single(UnitCatalog.Read(new(definition, changed), rules, texts, 4).Rows).Cells[^1].Text);
        var detail = UnitDetails.Read(view, rules, texts, 77);
        Assert.Equal("officer.1队", detail.Name);
        Assert.Equal("32000/20000", detail.Soldiers);
        var rankedDefinition = definition with { Information = definition.Information with {
            Ranks = [new(10, "rank", 45000, 0, 0, 0)],
            Offices = [new(20, "office", 15000, 0)] } };
        var rankedWorld = world with {
            Forces = world.Forces.Select(f => f.Id == 1 ? f with { RankId = 10 } : f).ToArray(),
            Officers = world.Officers.Select(o => o.Id == 1 ? o with { OfficeId = 20 } : o).ToArray()
        };
        Assert.Equal("32000/45000", UnitDetails.Read(new(rankedDefinition, rankedWorld), rules, texts, 77).Soldiers);
        var appointedWorld = rankedWorld with { Forces = rankedWorld.Forces.Select(f => f.Id == 1 ? f with { LeaderId = 2 } : f).ToArray() };
        Assert.Equal("32000/15000", UnitDetails.Read(new(rankedDefinition, appointedWorld), rules, texts, 77).Soldiers);
        var unappointedWorld = appointedWorld with { Officers = appointedWorld.Officers.Select(o => o with { OfficeId = null }).ToArray() };
        Assert.Equal("32000/10000", UnitDetails.Read(new(rankedDefinition, unappointedWorld), rules, texts, 77).Soldiers);
        Assert.Equal(Assert.Single(Read(2).Rows).Cells.Skip(1), detail.Land);
        Assert.Equal(Assert.Single(Read(3).Rows).Cells.Skip(1), detail.Water);
        Assert.Equal(1, Assert.Single(detail.Board.Members).OfficerId);
        Assert.Equal(new[] { 1, 5 }, detail.OfficerIds);
        Assert.Equal(new[] { 1, 5 }, OfficerCatalog.Read(view, texts, 0, null, null, unitFilter: 77).Rows.Select(r => r.Key.Id).Order());
        var browser = new InformationBrowser();
        browser.Open(InformationCategory.Units); browser.ShowDetail(77);
        browser.Navigate(new("武将", InformationCategory.Officers, UnitId: 77));
        Assert.Equal(77, browser.UnitFilter);
        browser.Back();
        Assert.Null(browser.UnitFilter); Assert.True(browser.IsDetail); Assert.Equal(77, browser.SelectedId);
        StateAssert.Equal(hash, world);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForeignVisibilityMatchesCatalogDetailAndMapSummary(bool orders, bool tactics)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        definition = definition with
        {
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, null)],
                [new(100, 5, 0, 100000, 5, new(1, 1), TravelMode.Land) { Tactics = new(15, 0, 2, [], []) }], [])
                { Tactics = [new(2, 0, TacticEffect.Martial, 10, 9, 0, true, false, [100])] },
            Information = definition.Information with { Tactics = [new(2, "tactic")] }
        };
        world = world with
        {
            Simulation = world.Simulation with { OtherForceOrdersVisible = orders, OtherForceTacticsVisible = tactics },
            Units = [new(77, 2, new(12, 12), 1000) { FormationId = 100, CommanderId = 4, ServicePlaceId = 2,
                Order = new(MilitaryOrderKind.AttackSettlement, null, 1) }],
            Officers = world.Officers.Select(o => o.Id == 4 ? o with
                { UnitId = 77, SettlementId = null, EquippedTacticId = 2 } : o).ToArray()
        };
        var view = new StrategySnapshot(definition, world);
        var keys = definition.Officers.Select(o => o.NameKey).Concat(definition.Forces.Select(f => f.NameKey))
            .Concat(definition.Settlements.Select(s => s.NameKey));
        var entries = keys.ToDictionary(key => key, key => key);
        entries["tactic"] = "奋迅";
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, entries);
        var row = Assert.Single(UnitCatalog.Read(view, null, texts, 4).Rows);
        var detail = UnitDetails.Read(view, view.MilitaryRules, texts, 77);
        var summary = MapCommandSummary.Unit(view, texts, 77);
        Assert.Equal(orders ? "攻击" : "＊＊", row.Cells[1].Text);
        Assert.Equal(orders ? "city.1" : "＊＊＊＊＊", row.Cells[2].Text);
        Assert.Equal(row.Cells[1].Text, detail.Orders[0].Value);
        Assert.Equal(row.Cells[2].Text, detail.Orders[1].Value);
        Assert.Equal(detail.Orders.Select(f => f.Value), summary.Rows.Skip(5).Select(r => r.Value));
        Assert.Equal(tactics ? "奋迅" : "＊＊＊", Assert.Single(detail.Board.Members).Tactic);
        Assert.All(row.Cells.Skip(3), cell => Assert.True(cell.IsUnavailable));
    }
}
